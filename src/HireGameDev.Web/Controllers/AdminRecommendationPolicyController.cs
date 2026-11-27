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
        var result = await policies!.GetAsync(ct);
        if (result.IsFailure) return StatusCode(503, "Recommendation settings are invalid; contact an administrator.");
        return View(result.Value);
    }

    [HttpPost(""), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Update(int? minimumScore, int? minimumCoverage, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound();
        if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || !minimumScore.HasValue || !minimumCoverage.HasValue || string.IsNullOrWhiteSpace(reason)) return BadRequest();
        var result = await policies!.UpdateAsync(Actor, new RecommendationPolicy(minimumScore.Value, minimumCoverage.Value), reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
        TempData["RecommendationPolicyMessage"] = "Recommendation thresholds updated.";
        return RedirectToAction(nameof(Index));
    }
}
