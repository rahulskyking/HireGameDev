using System.Security.Claims;
using HireGameDev.Application.Abstractions;
using HireGameDev.Domain.Entities;
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

// Development-only intake: recording a request never deletes or disables an account.
[Authorize]
[Route("account/erasure-request")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ErasureRequestsController(AppDbContext db, IClock clock, IWebHostEnvironment environment, IPortalNavigator portals) : Controller
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers["Referrer-Policy"] = "no-referrer"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        Private(); if (!environment.IsDevelopment()) return NotFound();
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.IsActive && u.DeletedAt == null, ct)) return Forbid();
        var requests = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ActorUserId == Actor && a.ActorType == AuditActorType.User &&
                a.EntityType == "erasure_request" && a.Action == "account.erasure_requested")
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new ErasureRequestStatus(a.CreatedAt,
                db.AuditLogs.Any(r => r.EntityType == "erasure_request" && r.EntityId == a.EntityId &&
                    r.Action == "account.erasure_request_reviewed" && r.ActorType == AuditActorType.Admin)))
            .Take(10).ToListAsync(ct);
        return View(requests);
    }

    [HttpPost(""), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> Submit(bool confirmed, CancellationToken ct)
    {
        Private(); if (!environment.IsDevelopment()) return NotFound();
        if (!ModelState.IsValid || !confirmed) return BadRequest();
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.IsActive && u.DeletedAt == null, ct)) return Forbid();
        db.AuditLogs.Add(new AuditLog { ActorUserId = Actor, ActorType = AuditActorType.User,
            Action = "account.erasure_requested", EntityType = "erasure_request", EntityId = Guid.NewGuid().ToString(), CreatedAt = clock.UtcNow });
        await db.SaveChangesAsync(ct);
        return View("Submitted");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpGet("review")]
    public async Task<IActionResult> Review(CancellationToken ct)
    {
        Private(); if (!environment.IsDevelopment() || portals.ResolvePortal(HttpContext) != PortalSite.Landing) return NotFound();
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.IsActive && u.DeletedAt == null, ct)) return Forbid();
        var requests = await (from request in db.AuditLogs.AsNoTracking()
            join user in db.Users.AsNoTracking() on request.ActorUserId equals (Guid?)user.Id
            where request.Action == "account.erasure_requested" && request.EntityType == "erasure_request"
                && !db.AuditLogs.Any(review => review.EntityType == "erasure_request" && review.EntityId == request.EntityId && review.Action == "account.erasure_request_reviewed")
            orderby request.CreatedAt, request.Id
            select new ErasureRequestRow(request.EntityId, user.Id, user.Email, request.CreatedAt)).Take(100).ToListAsync(ct);
        return View(requests);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("review"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateSettingsWrite)]
    public async Task<IActionResult> MarkReviewed(string? requestId, string? reason, CancellationToken ct)
    {
        Private(); if (!environment.IsDevelopment() || portals.ResolvePortal(HttpContext) != PortalSite.Landing) return NotFound();
        reason = reason?.Trim();
        if (!ModelState.IsValid || !Guid.TryParse(requestId, out var id) || id == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 500) return BadRequest();
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.IsActive && u.DeletedAt == null, ct)) return Forbid();
        var key = id.ToString();
        if (!await db.AuditLogs.AsNoTracking().AnyAsync(a => a.EntityType == "erasure_request" && a.EntityId == key && a.Action == "account.erasure_requested", ct)) return NotFound();
        if (!await db.AuditLogs.AsNoTracking().AnyAsync(a => a.EntityType == "erasure_request" && a.EntityId == key && a.Action == "account.erasure_request_reviewed", ct))
        {
            db.AuditLogs.Add(new AuditLog { ActorUserId = Actor, ActorType = AuditActorType.Admin, Action = "account.erasure_request_reviewed", EntityType = "erasure_request", EntityId = key, Reason = reason, CreatedAt = clock.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        return RedirectToAction(nameof(Review));
    }
}

public sealed record ErasureRequestRow(string RequestId, Guid UserId, string? Email, DateTimeOffset RequestedAt);

public sealed record ErasureRequestStatus(DateTimeOffset RequestedAt, bool Reviewed);
