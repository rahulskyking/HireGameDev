using System.Security.Claims;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HireGameDev.Application.Common;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Jobs;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.StudioMember)]
[Route("Studio/Jobs")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudioJobsController(IJobLifecycleService jobs, IStudioProfileService studios,
    IReferenceDataService references, IJobPublishChecker checker, AppDbContext db, IClock clock, IPlanEntitlementService? plans = null, IStudioPostingUsageService? postingUsage = null) : Controller
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private async Task<StudioMembershipSummary?> Membership(Guid studioId, bool write, CancellationToken ct)
    {
        var memberships = await studios.GetUserMembershipsAsync(Actor, ct);
        return memberships.FirstOrDefault(x => x.StudioId == studioId &&
            (!write || x.MemberRole is StudioMemberRole.Owner or StudioMemberRole.Recruiter));
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? studioId, JobStatus? status, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest();
        var memberships = await studios.GetUserMembershipsAsync(Actor, cancellationToken);
        if (memberships.Count == 0) return RedirectToAction("Onboarding", "Studio");
        var studio = studioId.HasValue ? memberships.FirstOrDefault(x => x.StudioId == studioId) : memberships[0];
        if (studio is null) return Forbid();
        var result = await jobs.ListAsync(Actor, studio.StudioId, status, page, cancellationToken);
        if (result.IsFailure) return Forbid();
        if (plans is not null)
        {
            // Advisory only; activation re-reads under the transaction and quota locks.
            var entitlement = await plans.GetAsync("studio", studio.StudioId, cancellationToken);
            var now = clock.UtcNow.ToUniversalTime();
            var occupied = await db.Jobs.AsNoTracking().CountAsync(j => j.StudioId == studio.StudioId &&
                (j.Status == JobStatus.Active || j.Status == JobStatus.Paused || j.Status == JobStatus.Full) && j.ExpiresAt > now, cancellationToken);
            var postingResult = postingUsage is null ? null : await postingUsage.GetAsync(Actor, studio.StudioId, cancellationToken);
            ViewData["StudioPlanUsage"] = new StudioPlanUsage(entitlement.Plan, occupied, entitlement.Limit,
                postingResult?.IsSuccess == true ? postingResult.Value : null);
        }
        return View(new JobListPage(studio, memberships, result.Value, status));
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(Guid studioId, CancellationToken cancellationToken)
    {
        if (await Membership(studioId, true, cancellationToken) is null) return Forbid();
        var model = new JobEditorModel { StudioId = studioId };
        await Populate(model, cancellationToken);
        return View("Edit", model);
    }

    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, Guid studioId, CancellationToken cancellationToken)
    {
        if (await Membership(studioId, true, cancellationToken) is null) return Forbid();
        var result = await jobs.GetAsync(Actor, studioId, id, cancellationToken);
        if (result.IsFailure) return NotFound();
        var job = result.Value;
        if (job.Status is not (JobStatus.Draft or JobStatus.Paused)) return RedirectToAction(nameof(Manage), new { id, studioId });
        var model = new JobEditorModel { StudioId = studioId, JobId = id, Version = job.Version, Content = job.Content,
            Skills = job.Content.Skills.Select(x => new JobSkillRow { SkillId = x.SkillId, IsRequired = x.IsRequired, MinProficiency = x.MinProficiency }).ToList() };
        await Populate(model, cancellationToken);
        return View(model);
    }

    [HttpPost("Save"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.StudioJobsWrite)]
    public async Task<IActionResult> Save(JobEditorModel model, string? destination, CancellationToken cancellationToken)
    {
        if (await Membership(model.StudioId, true, cancellationToken) is null) return Forbid();
        if (ModelState.IsValid)
        {
            var content = model.Content with { Skills = model.Skills.Where(x => x.SkillId.HasValue)
                .Select(x => new JobSkillInput(x.SkillId!.Value, x.IsRequired, x.MinProficiency)).ToArray() };
            var result = await jobs.SaveAsync(new(Actor, model.StudioId, content, model.JobId, model.Version), cancellationToken);
            if (result.IsSuccess)
            {
                TempData["StatusMessage"] = "Job saved.";
                return RedirectToAction(destination == "preview" ? nameof(Preview) : destination == "manage" ? nameof(Manage) : nameof(Edit), new { id = result.Value.Id, studioId = model.StudioId });
            }
            ModelState.AddModelError("", result.Error.Message);
            if (result.Error.Code == "Jobs.StaleVersion") Response.StatusCode = StatusCodes.Status409Conflict;
        }
        model.HasUnsavedChanges = true;
        model.AutosaveStopped = true; // Never retry a failed manual save in the background.
        await Populate(model, cancellationToken);
        return View("Edit", model);
    }

    [HttpPost("Autosave"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.StudioJobsWrite)]
    public async Task<IActionResult> Autosave(JobEditorModel model, CancellationToken cancellationToken)
    {
        if (await Membership(model.StudioId, true, cancellationToken) is null)
            return StatusCode(403, new { code = "Jobs.Forbidden", message = "Your write access changed. Autosave stopped." });
        if (!ModelState.IsValid || !model.JobId.HasValue || !model.Version.HasValue)
            return BadRequest(new { code = "Jobs.InvalidAutosave", message = "Autosave needs an existing draft and version. Save manually or reload." });
        var content = model.Content with { Skills = model.Skills.Where(x => x.SkillId.HasValue)
            .Select(x => new JobSkillInput(x.SkillId!.Value, x.IsRequired, x.MinProficiency)).ToArray() };
        var result = await jobs.SaveAsync(new(Actor, model.StudioId, content, model.JobId, model.Version), cancellationToken);
        if (result.IsFailure)
        {
            var status = result.Error.Type switch
            {
                ErrorType.Forbidden => 403, ErrorType.NotFound => 404, ErrorType.RateLimited => 429,
                ErrorType.Conflict => 409, _ => result.Error.Code == "Jobs.NotEditable" ? 409 : 422
            };
            return StatusCode(status, new { code = result.Error.Code, message = result.Error.Message });
        }
        return Json(new { version = result.Value.Version, jobId = result.Value.Id });
    }

    [HttpPost("{id:guid}/Duplicate"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.StudioJobsWrite)]
    public async Task<IActionResult> Duplicate(Guid id, Guid studioId, CancellationToken cancellationToken)
    {
        if (await Membership(studioId, true, cancellationToken) is null) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        var result = await jobs.DuplicateAsync(Actor, studioId, id, cancellationToken);
        if (result.IsFailure)
            return await Rejected(id, studioId, result.Error.Message, cancellationToken);
        TempData["StatusMessage"] = "Copy created as a new draft. Review the details before publishing; duplicate and cooldown checks still apply.";
        return RedirectToAction(nameof(Edit), new { id = result.Value.Id, studioId });
    }

    [HttpGet("Preview/{id:guid}")]
    public async Task<IActionResult> Preview(Guid id, Guid studioId, CancellationToken cancellationToken)
    {
        // These headers apply even to rejected preview requests; never cache Studio drafts.
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (!ModelState.IsValid) return BadRequest();
        var member = await Membership(studioId, false, cancellationToken);
        if (member is null) return Forbid();
        var result = await jobs.GetAsync(Actor, studioId, id, cancellationToken);
        if (result.IsFailure) return NotFound();
        var job = result.Value;
        var canWrite = member.MemberRole is StudioMemberRole.Owner or StudioMemberRole.Recruiter;
        var skillNames = (await references.GetActiveSkillsAsync(cancellationToken: cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        var skills = job.Content.Skills.Select(x => new JobPreviewSkill(
            skillNames.GetValueOrDefault(x.SkillId) ?? $"Unavailable skill (ID {x.SkillId})", x.IsRequired, x.MinProficiency)).ToArray();
        var city = job.Content.CityId.HasValue
            ? (await references.GetActiveCitiesAsync(cancellationToken)).FirstOrDefault(x => x.Id == job.Content.CityId)?.Name ?? "Unavailable city"
            : null;
        // Never accept preview content or a Studio ownership assertion from the request.
        // The checker is read-only and runs only after authoritative membership/job checks.
        var assessment = canWrite ? await checker.CheckAsync(new(job.Id, job.StudioId, job.Content), cancellationToken) : null;
        var effectiveStatus = job.ExpiresAt.HasValue && job.ExpiresAt <= DateTimeOffset.UtcNow && (job.Status is JobStatus.Active or JobStatus.Paused or JobStatus.Full)
            ? JobStatus.Expired : job.Status;
        JobLifecycleAction? action = effectiveStatus switch
        {
            JobStatus.Draft => JobLifecycleAction.Publish, JobStatus.Paused => JobLifecycleAction.Resume,
            JobStatus.Full => JobLifecycleAction.ReopenFull, JobStatus.Expired => JobLifecycleAction.Renew, _ => null
        };
        return View(new JobPreviewPage(job, member, city, skills, canWrite, assessment, canWrite ? action : null));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Manage(Guid id, Guid studioId, CancellationToken cancellationToken)
    {
        var member = await Membership(studioId, false, cancellationToken);
        if (member is null) return Forbid();
        var result = await jobs.GetAsync(Actor, studioId, id, cancellationToken);
        if (result.IsFailure) return NotFound();
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return View(new JobManagePage(result.Value, member.MemberRole is StudioMemberRole.Owner or StudioMemberRole.Recruiter));
    }

    [HttpPost("{id:guid}/Action"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.StudioJobsWrite)]
    public async Task<IActionResult> Transition(Guid id, Guid studioId, [BindRequired] JobLifecycleAction lifecycleAction, [BindRequired] uint version,
        bool confirmed, CancellationToken cancellationToken)
    {
        if (await Membership(studioId, true, cancellationToken) is null) return Forbid();
        if (!ModelState.IsValid || !Enum.IsDefined(lifecycleAction))
            return await Rejected(id, studioId, "The action could not be read. Reload this page and try again.", cancellationToken);
        if (!confirmed)
            return await Rejected(id, studioId, "Confirm the action before continuing. No status change was requested.", cancellationToken);

        var result = await jobs.TransitionAsync(new(Actor, studioId, id, lifecycleAction, version), cancellationToken);
        if (result.IsFailure)
            return await Rejected(id, studioId, result.Error.Message, cancellationToken);
        if (!result.Value.Applied)
        {
            // Keep structured findings in the response, rather than flattening them into a TempData cookie.
            // A successful Result means evaluated, NOT published. Applied is the business outcome.
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            return View("Manage", new JobManagePage(result.Value.Job, true, result.Value.Findings,
                $"{lifecycleAction} was not completed. The job is {result.Value.Job.Status}."));
        }
        TempData["StatusMessage"] = $"{lifecycleAction} completed. The job is now {result.Value.Job.Status}.";
        if (result.Value.Findings.Count > 0)
            TempData["Warnings"] = string.Join(" ", result.Value.Findings.Select(x => x.Message));
        return RedirectToAction(nameof(Manage), new { id, studioId });
    }

    private async Task<IActionResult> Rejected(Guid id, Guid studioId, string error, CancellationToken ct)
    {
        var current = await jobs.GetAsync(Actor, studioId, id, ct);
        if (current.IsFailure) return NotFound();
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return View("Manage", new JobManagePage(current.Value, true, Error: error));
    }

    [HttpPost("{id:guid}/Delete"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.StudioJobsWrite)]
    public async Task<IActionResult> Delete(Guid id, Guid studioId, bool confirmed, CancellationToken cancellationToken)
    {
        if (await Membership(studioId, true, cancellationToken) is null) return Forbid();
        if (!ModelState.IsValid || !confirmed) return BadRequest("Confirm permanent draft deletion.");
        var result = await jobs.DeleteDraftAsync(Actor, studioId, id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
            return RedirectToAction(nameof(Manage), new { id, studioId });
        }
        TempData["StatusMessage"] = "Draft permanently deleted.";
        return RedirectToAction(nameof(Index), new { studioId });
    }

    private async Task Populate(JobEditorModel model, CancellationToken ct)
    {
        model.Cities = await references.GetActiveCitiesAsync(ct);
        model.AvailableSkills = await references.GetActiveSkillsAsync(cancellationToken: ct);
        while (model.Skills.Count < 12) model.Skills.Add(new JobSkillRow());
    }
}
