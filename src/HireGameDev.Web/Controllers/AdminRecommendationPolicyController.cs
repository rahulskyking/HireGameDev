using System.Security.Claims;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Domain.Constants;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HireGameDev.Web.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("Admin/Recommendations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminRecommendationPolicyController(IWebHostEnvironment environment, IPortalNavigator portals, AppDbContext db, IRecommendationPolicyService? policies = null) : Controller
{
    private bool Enabled => environment.IsDevelopment() && portals.ResolvePortal(HttpContext) == PortalSite.Landing && policies is not null;
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers["Referrer-Policy"] = "no-referrer"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }
    private Task<bool> IsActiveAdmin(CancellationToken ct) => (from u in db.Users.AsNoTracking()
        join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
        join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
        where u.Id == Actor && u.IsActive && u.DeletedAt == null && r.NormalizedName == HireGameDevRoleNames.Admin.ToUpperInvariant()
        select u.Id).AnyAsync(ct);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound();
        if (!await IsActiveAdmin(ct)) return Forbid();
        var candidate = await policies!.GetAsync(ct);
        var studio = await policies.GetStudioAsync(ct);
        // Allow an Admin to repair a malformed setting without silently using a default.
        return View(new RecommendationPolicyAdminPage(candidate.IsSuccess ? candidate.Value : null, studio.IsSuccess ? studio.Value : null));
    }

    [HttpPost(""), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Update(string? direction, int? minimumScore, int? minimumCoverage, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound();
        if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || (direction is not ("candidate" or "studio")) || !minimumScore.HasValue || !minimumCoverage.HasValue || string.IsNullOrWhiteSpace(reason)) return BadRequest();
        var policy = new RecommendationPolicy(minimumScore.Value, minimumCoverage.Value);
        var result = direction == "candidate"
            ? await policies!.UpdateAsync(Actor, policy, reason, ct)
            : await policies!.UpdateStudioAsync(Actor, policy, reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
        TempData["RecommendationPolicyMessage"] = "Recommendation thresholds updated.";
        return RedirectToAction(nameof(Index));
    }
}

public sealed record RecommendationPolicyAdminPage(RecommendationPolicy? Candidate, RecommendationPolicy? Studio);
