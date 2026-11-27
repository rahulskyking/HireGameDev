using System.Security.Claims;
using System.Text;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Applications;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.CandidateOnly)]
[Route("candidate/applications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CandidateApplicationsController(IApplicationSubmissionService submission, ICandidateApplicationListService list,
    ICandidateApplicationDetailService detail, IApplicationWithdrawalService withdrawal, IWebHostEnvironment environment,
    IWithdrawnApplicationSnapshotDeletionService? snapshotDeletion = null) : Controller
{
    private bool Enabled => ApplicationFeature.Enabled(environment);
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; Response.Headers["Referrer-Policy"] = "no-referrer"; }

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        Private();
        if (!Enabled) return NotFound();
        if (!ModelState.IsValid) return BadRequest();
        var result = await list.ListAsync(Actor, page, 25, cancellationToken);
        if (result.IsFailure) return result.Error.Code == "Applications.InvalidPage" ? BadRequest() : StatusCode(503);
        return View(result.Value);
    }

    [HttpGet("{applicationId:guid}")]
    public async Task<IActionResult> Detail(Guid applicationId, CancellationToken cancellationToken)
    {
        Private(); if (!Enabled) return NotFound();
        var result = await detail.GetAsync(Actor, applicationId, cancellationToken);
        return result.IsSuccess ? View(result.Value) : NotFound();
    }

    [HttpPost("{jobId:guid}/review"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.ApplicationWrite)]
    public async Task<IActionResult> Review(Guid jobId, string? screeningAnswer, CancellationToken cancellationToken, string? jobSlug = null)
    {
        Private(); if (!Enabled) return NotFound();
        if (!ModelState.IsValid || screeningAnswer?.Length > 2000) return BadRequest();
        var result = await submission.PrepareAsync(Actor, jobId, screeningAnswer, cancellationToken);
        if (result.IsFailure) return View("Retry", new ApplicationRetryPage(jobId, SafeSlug(jobSlug), screeningAnswer, result.Error.Message));
        return View(new ApplicationReviewPage(jobId, SafeSlug(jobSlug), screeningAnswer, result.Value));
    }

    [HttpPost("{jobId:guid}/submit"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.ApplicationWrite)]
    public async Task<IActionResult> Submit(Guid jobId, string? screeningAnswer, string? reviewToken, bool consent, CancellationToken cancellationToken, string? jobSlug = null, string? encodedAnswer = null)
    {
        Private(); if (!Enabled) return NotFound();
        if (!ModelState.IsValid || encodedAnswer?.Length > 12000 || reviewToken?.Length > 8192) return BadRequest();
        if (encodedAnswer is not null)
        {
            try { screeningAnswer = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encodedAnswer)); }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { return BadRequest(); }
        }
        if (screeningAnswer?.Length > 2000) return BadRequest();
        var result = await submission.SubmitAsync(new SubmitApplicationCommand(Actor, jobId, screeningAnswer, reviewToken ?? "", consent), cancellationToken);
        if (result.IsFailure) return View("Retry", new ApplicationRetryPage(jobId, SafeSlug(jobSlug), screeningAnswer, result.Error.Message));
        TempData["ApplicationMessage"] = "Application submitted.";
        return RedirectToAction(nameof(Detail), new { applicationId = result.Value.ApplicationId });
    }

    // Slug is only a navigation hint; it never authorizes or selects the application target.
    private static string? SafeSlug(string? slug) => !string.IsNullOrEmpty(slug) && slug.Length <= 160 &&
        slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') ? slug : null;

    [HttpGet("{applicationId:guid}/delete-snapshot")]
    public async Task<IActionResult> ConfirmSnapshotDeletion(Guid applicationId, CancellationToken cancellationToken)
    {
        Private(); if (!Enabled || snapshotDeletion is null) return NotFound();
        var application = await detail.GetAsync(Actor, applicationId, cancellationToken);
        if (application.IsFailure || application.Value.Status != HireGameDev.Domain.Enums.ApplicationStatus.Withdrawn || application.Value.SnapshotPurged) return NotFound();
        return View(applicationId);
    }

    [HttpPost("{applicationId:guid}/delete-snapshot"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.ApplicationWrite)]
    public async Task<IActionResult> DeleteSnapshot(Guid applicationId, bool confirmed, CancellationToken cancellationToken)
    {
        Private(); if (!Enabled || snapshotDeletion is null) return NotFound();
        if (!ModelState.IsValid || !confirmed) return BadRequest();
        var result = await snapshotDeletion.DeleteAsync(Actor, applicationId, cancellationToken);
        TempData["ApplicationMessage"] = result.IsSuccess
            ? (result.Value ? "Your submitted snapshot was deleted." : "Your submitted snapshot was already deleted.")
            : "The snapshot could not be deleted. Please try again or contact us.";
        return RedirectToAction(nameof(Detail), new { applicationId });
    }

    [HttpGet("{applicationId:guid}/withdraw")]
    public IActionResult ConfirmWithdrawal(Guid applicationId)
    {
        Private(); if (!Enabled) return NotFound();
        // Generic confirmation only; ownership and state are rechecked on POST.
        return View(applicationId);
    }

    [HttpPost("{applicationId:guid}/withdraw"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.ApplicationWrite)]
    public async Task<IActionResult> Withdraw(Guid applicationId, CancellationToken cancellationToken)
    {
        Private(); if (!Enabled) return NotFound();
        var result = await withdrawal.WithdrawAsync(Actor, applicationId, cancellationToken);
        TempData["ApplicationMessage"] = result.IsSuccess ? "Application withdrawn. Studio access to the snapshot is revoked." : "The application could not be withdrawn.";
        return RedirectToAction(nameof(Index));
    }
}
