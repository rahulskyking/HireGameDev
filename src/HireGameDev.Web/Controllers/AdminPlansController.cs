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
        return View(new PlanAdminPage(limits.Where(x => x.Plan == "free").ToArray(), await searchQuotas!.GetSettingsAsync(ct), draft.IsSuccess ? draft.Value : null));
    }

    [HttpPost("limit"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Limit(string audience, string plan, int? limit, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || plan != "free" || !limit.HasValue) return BadRequest();
        var result = await plans!.SetLimitAsync(Actor, audience, plan, limit.Value, reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
        TempData["PlanMessage"] = "Free limit updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("search-limits"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> SearchLimits(int? candidateDaily, int? studioDaily, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason)) return BadRequest();
        var result = await searchQuotas!.UpdateSettingsAsync(Actor, new SearchQuotaSettings(candidateDaily, studioDaily), reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
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
        if (!ModelState.IsValid) return BadRequest();
        var result = await catalog.UpdateAsync(Actor, audience, plan, feature, value, version, reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
        TempData["CatalogMessage"] = audience == "studio" && plan == "free" && feature == "postings"
            ? "Free Studio posting limit updated."
            : "Proposed setting saved. Live limits are unchanged.";
        return RedirectToAction(nameof(Catalog));
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

public sealed record PlanAdminPage(IReadOnlyList<PlanLimit> Limits, SearchQuotaSettings SearchLimits, SearchAllowanceDraft? SearchDraft);
