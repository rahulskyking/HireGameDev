using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
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
    IReferenceDataService references) : Controller
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
                return RedirectToAction(destination == "manage" ? nameof(Manage) : nameof(Edit), new { id = result.Value.Id, studioId = model.StudioId });
            }
            ModelState.AddModelError("", result.Error.Message);
            if (result.Error.Code == "Jobs.StaleVersion") Response.StatusCode = StatusCodes.Status409Conflict;
        }
        await Populate(model, cancellationToken);
        return View("Edit", model);
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
