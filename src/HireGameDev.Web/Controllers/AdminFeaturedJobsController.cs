using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
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
[Route("Admin/FeaturedJobs")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminFeaturedJobsController(IWebHostEnvironment environment, IPortalNavigator portals, AppDbContext db, IFeaturedJobPlacementService? placements = null) : Controller
{
    private bool Enabled => environment.IsDevelopment() && portals.ResolvePortal(HttpContext) == PortalSite.Landing && placements is not null;
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["Referrer-Policy"] = "no-referrer"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }
    private Task<bool> IsActiveAdmin(CancellationToken ct) => (from u in db.Users.AsNoTracking() join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
        join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
        where u.Id == Actor && u.IsActive && u.DeletedAt == null && r.NormalizedName == HireGameDevRoleNames.Admin.ToUpperInvariant() select u.Id).AnyAsync(ct);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        search = search?.Trim() ?? ""; if (search.Length > 100) return BadRequest();
        var found = new List<FeaturedJobSearchItem>();
        if (search.Length >= 3)
        {
            var term = search.ToLowerInvariant();
            var rows = await (from j in db.Jobs.AsNoTracking() join s in db.StudioProfiles.AsNoTracking() on j.StudioId equals s.Id
                where (j.Title != null && j.Title.ToLower().Contains(term)) || j.Slug.ToLower().Contains(term) || s.Name.ToLower().Contains(term)
                orderby j.UpdatedAt descending, j.Id
                select new { j.Id, j.Title, Studio = s.Name, j.Status })
                .Take(25).ToListAsync(ct);
            found = rows.Select(j => new FeaturedJobSearchItem(j.Id, j.Title ?? "Untitled job", j.Studio, j.Status.ToString())).ToList();
        }
        return View(new FeaturedJobsAdminPage(search, found, await placements!.ListAsync(ct)));
    }

    [HttpPost("set"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Set(Guid jobId, bool enabled, int days, string? reason, CancellationToken ct)
    {
        Private(); if (!Enabled) return NotFound(); if (!await IsActiveAdmin(ct)) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        var result = await placements!.SetAsync(Actor, jobId, enabled, days, reason, ct);
        if (result.IsFailure) return BadRequest(result.Error.Message);
        TempData["PlacementMessage"] = enabled ? "Placement scheduled." : "Placement removed.";
        return RedirectToAction(nameof(Index));
    }
}

public sealed record FeaturedJobSearchItem(Guid Id, string Title, string Studio, string Status);
public sealed record FeaturedJobsAdminPage(string Search, IReadOnlyList<FeaturedJobSearchItem> Results, IReadOnlyList<FeaturedJobAdminItem> Placements);
