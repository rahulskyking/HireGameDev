using System.Security.Claims;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Admin;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

/// <summary>
/// ADR-012 Decision #3 &amp; ADR-010: Admin panel is served strictly on hiregamedev.com/Admin
/// (and bare localhost in single-host development) with mandatory audit reasons on all mutations.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("Admin")]
public sealed class AdminController : Controller
{
    private readonly IStudioVerificationService _studioVerificationService;
    private readonly IPortalNavigator _portalNavigator;

    public AdminController(
        IStudioVerificationService studioVerificationService,
        IPortalNavigator portalNavigator)
    {
        _studioVerificationService = studioVerificationService;
        _portalNavigator = portalNavigator;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        if (_portalNavigator.ResolvePortal(HttpContext) != PortalSite.Landing)
        {
            return NotFound();
        }

        var pendingQueue = await _studioVerificationService.GetAdminQueueAsync(
            StudioVerificationStatus.Pending,
            cancellationToken);

        var vm = new AdminDashboardViewModel
        {
            StudioCounts = pendingQueue.Counts,
            PastSlaPendingCount = pendingQueue.Items.Count(i => i.IsPastTargetSla)
        };

        return View(vm);
    }

    [HttpGet("Studios/Verification")]
    public async Task<IActionResult> StudioVerification(
        string? status = "pending",
        CancellationToken cancellationToken = default)
    {
        if (_portalNavigator.ResolvePortal(HttpContext) != PortalSite.Landing)
        {
            return NotFound();
        }

        var (parsedFilter, tabKey) = ParseStatusFilter(status);
        var queue = await _studioVerificationService.GetAdminQueueAsync(parsedFilter, cancellationToken);

        return View("StudioVerification", new AdminStudioVerificationPageViewModel
        {
            Queue = queue,
            ActiveTabKey = tabKey
        });
    }

    [HttpPost("Studios/Verification/Review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewStudioVerification(
        ReviewStudioVerificationInputModel? input,
        CancellationToken cancellationToken = default)
    {
        if (_portalNavigator.ResolvePortal(HttpContext) != PortalSite.Landing)
        {
            return NotFound();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminUserId) ||
            adminUserId == Guid.Empty)
        {
            return RedirectToAction("Login", "Account");
        }

        input ??= new ReviewStudioVerificationInputModel();
        var (parsedFilter, tabKey) = ParseStatusFilter(input.ReturnFilter);

        var reviewResult = await _studioVerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: adminUserId,
                StudioId: input.StudioId,
                Decision: input.Decision,
                Reason: input.Reason ?? string.Empty,
                ReviewerFeedback: input.ReviewerFeedback),
            cancellationToken);

        if (reviewResult.IsFailure)
        {
            ModelState.AddModelError(string.Empty, reviewResult.Error.Message);
            var queue = await _studioVerificationService.GetAdminQueueAsync(parsedFilter, cancellationToken);

            return View("StudioVerification", new AdminStudioVerificationPageViewModel
            {
                Queue = queue,
                ActiveTabKey = tabKey,
                ErrorStudioId = input.StudioId,
                FailedInput = input
            });
        }

        var outcome = reviewResult.Value;
        TempData["StatusMessage"] = input.Decision switch
        {
            StudioVerificationDecision.Approve =>
                $"Verified '{outcome.StudioName}' (/studios/{outcome.StudioSlug}). Audit entry recorded.",
            StudioVerificationDecision.ApproveIdentityChange =>
                $"Approved staged identity update for '{outcome.StudioName}' (/studios/{outcome.StudioSlug}). Audit entry recorded.",
            StudioVerificationDecision.RejectIdentityChange =>
                $"Rejected staged identity update for '{outcome.StudioName}' while keeping live Verified badge intact.",
            StudioVerificationDecision.Revoke =>
                $"Revoked verification for '{outcome.StudioName}'. Audit entry recorded.",
            _ =>
                $"Requested changes for '{outcome.StudioName}'. Studio Owner notified."
        };

        return RedirectToAction(nameof(StudioVerification), new { status = tabKey });
    }

    private static (StudioVerificationStatus? Filter, string TabKey) ParseStatusFilter(string? rawStatus)
    {
        var normalized = rawStatus?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "verified" => (StudioVerificationStatus.Verified, "verified"),
            "rejected" => (StudioVerificationStatus.Rejected, "rejected"),
            "unverified" => (StudioVerificationStatus.Unverified, "unverified"),
            "all" => (null, "all"),
            _ => (StudioVerificationStatus.Pending, "pending")
        };
    }
}
