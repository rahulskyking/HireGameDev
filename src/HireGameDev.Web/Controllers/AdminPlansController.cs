using System.Security.Claims;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Domain.Constants;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Portals;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("Admin/Plans")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPlansController(IWebHostEnvironment environment, IPortalNavigator portals, AppDbContext db,
    IPlanEntitlementService? plans = null, ISearchAllowanceDraftService? searchDrafts = null, ISearchQuotaService? searchQuotas = null, IPlanCatalogService? catalog = null) : Controller
{
    private bool Enabled => environment.IsDevelopment() && portals.ResolvePortal(HttpContext) == PortalSite.Landing && plans is not null && searchDrafts is not null && searchQuotas is not null;
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["Referrer-Policy"] = "no-referrer"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }
    private Task<bool> IsActiveAdmin(CancellationToken ct) => (from u in db.Users.AsNoTracking()
        join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
        where u.Id == Actor && u.IsActive && u.DeletedAt == null && r.NormalizedName == HireGameDevRoleNames.Admin.ToUpperInvariant() select u.Id).AnyAsync(ct);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        var limits = await plans!.GetLimitsAsync(ct);
        var draft = await searchDrafts!.GetAsync(ct);
        return View(new PlanAdminPage(limits.Where(x => x.Plan == "free").ToArray(), await searchQuotas!.GetSettingsAsync(ct),
            draft.IsSuccess ? draft.Value : null, await plans.PremiumEnabledAsync(ct)));
    }

    [HttpGet("studios")]
    public async Task<IActionResult> Studios(string? q, int page = 1, CancellationToken ct = default)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        q = q?.Trim();
        if (q?.Length > 100 || !ModelState.IsValid) return BadRequest();
        var query = db.StudioProfiles.AsNoTracking().Where(s => s.DeletedAt == null);
        if (!string.IsNullOrEmpty(q)) query = query.Where(s => EF.Functions.ILike(s.Name, "%" + q + "%") || EF.Functions.ILike(s.Slug, "%" + q + "%"));
        var total = await query.CountAsync(ct);
        const int pageSize = 25;
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pages);
        var studios = await query.OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new StudioPlanOption(s.Id, s.Name, s.Slug)).ToArrayAsync(ct);
        var assignments = (await plans!.GetManualStudioPlansAsync(studios.Select(s => s.Id).ToArray(), ct)).ToDictionary(x => x.StudioId, x => x.Plan);
        return View(new StudioPlanAssignmentsPage(studios, assignments, await plans.PremiumEnabledAsync(ct), q, page, pages, total));
    }

    [HttpPost("premium"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Premium(bool enabled, string reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason)) { TempData["PlanError"] = "Enter a reason before saving the Premium switch."; return RedirectToAction(nameof(Index), null, null, "plan-feedback"); }
        var result = await plans!.SetPremiumEnabledAsync(Actor, enabled, reason, ct);
        if (result.IsFailure) { TempData["PlanError"] = result.Error.Message; return RedirectToAction(nameof(Index), null, null, "plan-feedback"); }
        TempData["PlanMessage"] = enabled ? "Premium controls enabled. Billing remains unavailable." : "Premium controls disabled. Manual Studio assignments still apply.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("studio-assignment"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> StudioAssignment(Guid studioId, string plan, string reason, string? q, int page, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        // Only local route parameters are used after submission; cap them before redirecting.
        q = q?.Trim(); if (q?.Length > 100) q = null;
        page = Math.Clamp(page, 1, 100000);
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason))
        {
            TempData["StudioPlanError"] = "Choose a tier and provide a reason. No change was saved.";
            return RedirectToAction(nameof(Studios), new { q, page });
        }
        var result = await plans!.AssignAsync(Actor, "studio", studioId, plan, reason, ct);
        if (result.IsFailure)
        {
            TempData["StudioPlanError"] = result.Error.Message;
            return RedirectToAction(nameof(Studios), new { q, page });
        }
        TempData["StudioPlanMessage"] = plan == "free" ? "Studio override removed. Free rules now apply." : $"Studio assigned {plan.ToUpperInvariant()} manually. No payment was recorded.";
        return RedirectToAction(nameof(Studios), new { q, page });
    }
    [HttpPost("limit"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Limit(string audience, string plan, int? limit, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || plan != "free" || !limit.HasValue) { TempData["PlanError"] = "Enter a valid Free limit and reason."; return RedirectToAction(nameof(Index), null, null, "plan-feedback"); }
        var result = await plans!.SetLimitAsync(Actor, audience, plan, limit.Value, reason, ct);
        if (result.IsFailure) { TempData["PlanError"] = result.Error.Message; return RedirectToAction(nameof(Index), null, null, "plan-feedback"); }
        TempData["PlanMessage"] = "Free limit updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("search-limits"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> SearchLimits(int? candidateDaily, int? studioDaily, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason)) { TempData["PlanError"] = "Enter valid search limits and a reason."; return RedirectToAction(nameof(Index), null, null, "plan-feedback"); }
        var result = await searchQuotas!.UpdateSettingsAsync(Actor, new SearchQuotaSettings(candidateDaily, studioDaily), reason, ct);
        if (result.IsFailure) { TempData["PlanError"] = result.Error.Message; return RedirectToAction(nameof(Index), null, null, "plan-feedback"); }
        TempData["PlanMessage"] = "Search limits updated.";
        return RedirectToAction(nameof(Index));
    }


    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        Private(); if (!Enabled || catalog is null) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        return View(await catalog.GetAsync(ct));
    }

    [HttpPost("catalog"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> CatalogValue(string audience, string plan, string feature, string value, long version, string reason, CancellationToken ct)
    {
        Private(); if (!Enabled || catalog is null) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        var fragment = plan is "free" or "plus" or "pro" or "elite" ? plan : "top";
        if (!ModelState.IsValid) { TempData["CatalogError"] = "Enter a valid setting and reason."; return RedirectToAction(nameof(Catalog), null, null, "catalog-feedback"); }
        var result = await catalog.UpdateAsync(Actor, audience, plan, feature, value, version, reason, ct);
        if (result.IsFailure) { TempData["CatalogError"] = result.Error.Message; return RedirectToAction(nameof(Catalog), null, null, "catalog-feedback"); }
        TempData["CatalogMessage"] = "Setting saved. Only implemented quota features are enforced; other features remain planned.";
        TempData["CatalogSavedTier"] = fragment;
        return RedirectToAction(nameof(Catalog), null, null, fragment);
    }
    [HttpPost("search-draft"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> SearchDraft(int? candidateDaily, int? studioDaily, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason)) return BadRequest();
        var result = await searchDrafts!.UpdateAsync(Actor, new SearchAllowanceDraft(candidateDaily, studioDaily), reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
        TempData["PlanMessage"] = "Search limit draft saved. It is not active or enforced.";
        return RedirectToAction(nameof(Index));
    }
}

public sealed record StudioPlanOption(Guid Id, string Name, string Slug);
public sealed record PlanAdminPage(IReadOnlyList<PlanLimit> Limits, SearchQuotaSettings SearchLimits, SearchAllowanceDraft? SearchDraft, bool PremiumEnabled);
public sealed record StudioPlanAssignmentsPage(IReadOnlyList<StudioPlanOption> Studios, IReadOnlyDictionary<Guid, string> Assignments,
    bool PremiumEnabled, string? Query, int Page, int TotalPages, int TotalCount);
