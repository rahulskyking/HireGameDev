using System.Security.Claims;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
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
public sealed class AdminPlansController(IWebHostEnvironment environment, IPortalNavigator portals, AppDbContext db, IPlanEntitlementService? plans = null) : Controller
{
    private bool Enabled => environment.IsDevelopment() && portals.ResolvePortal(HttpContext) == PortalSite.Landing && plans is not null;
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["Referrer-Policy"] = "no-referrer"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }
    private Task<bool> IsActiveAdmin(CancellationToken ct) => (from u in db.Users.AsNoTracking()
        join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
        where u.Id == Actor && u.IsActive && u.DeletedAt == null && r.NormalizedName == HireGameDevRoleNames.Admin.ToUpperInvariant() select u.Id).AnyAsync(ct);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? audience, string? search, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound();
        if (!await IsActiveAdmin(ct)) return Forbid();
        audience ??= "candidate";
        search = search?.Trim() ?? "";
        if ((audience is not ("candidate" or "studio")) || search.Length > 100) return BadRequest();
        var targets = new List<PlanTarget>();
        if (search.Length >= 3)
        {
            var term = search.ToLowerInvariant();
            if (audience == "candidate")
            {
                var rows = await (from c in db.CandidateProfiles.AsNoTracking() join u in db.Users.AsNoTracking() on c.UserId equals u.Id
                    where c.DeletedAt == null && u.IsActive && u.DeletedAt == null && u.AccountType == AccountType.Candidate &&
                        (c.FullName.ToLower().Contains(term) || (u.Email != null && u.Email.ToLower().Contains(term)))
                    orderby c.FullName, c.Id select new { c.Id, Name = c.FullName, Detail = u.Email }).Take(25).ToArrayAsync(ct);
                foreach (var row in rows) targets.Add(new(row.Id, row.Name, row.Detail, (await plans!.GetAsync(audience, row.Id, ct)).Plan));
            }
            else
            {
                var rows = await db.StudioProfiles.AsNoTracking().Where(s => s.DeletedAt == null && s.Name.ToLower().Contains(term))
                    .OrderBy(s => s.Name).ThenBy(s => s.Id).Select(s => new { s.Id, s.Name, s.Slug }).Take(25).ToArrayAsync(ct);
                foreach (var row in rows) targets.Add(new(row.Id, row.Name, row.Slug, (await plans!.GetAsync(audience, row.Id, ct)).Plan));
            }
        }
        return View(new PlanAdminPage(await plans!.GetLimitsAsync(ct), audience, search, targets));
    }

    [HttpPost("limit"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Limit(string audience, string plan, int? limit, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid || !limit.HasValue) return BadRequest();
        var result = await plans!.SetLimitAsync(Actor, audience, plan, limit.Value, reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
        TempData["PlanMessage"] = "Plan limit updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("assign"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Assign(string audience, Guid profileId, string plan, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        var result = await plans!.AssignAsync(Actor, audience, profileId, plan, reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
        TempData["PlanMessage"] = "Plan assignment updated. Existing jobs and applications were not changed.";
        return RedirectToAction(nameof(Index), new { audience });
    }
}

public sealed record PlanTarget(Guid Id, string Name, string? Detail, string Plan);
public sealed record PlanAdminPage(IReadOnlyList<PlanLimit> Limits, string Audience, string Search, IReadOnlyList<PlanTarget> Targets);
