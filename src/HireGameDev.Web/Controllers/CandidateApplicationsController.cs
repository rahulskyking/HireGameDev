using System.Security.Claims;
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
    ICandidateApplicationDetailService detail, IApplicationWithdrawalService withdrawal, IWebHostEnvironment environment, IConfiguration configuration) : Controller
{
    private bool Enabled => ApplicationPilot.Enabled(environment, configuration);
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        Private(); if (!Enabled) return NotFound();
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
    public async Task<IActionResult> Review(Guid jobId, string? screeningAnswer, CancellationToken cancellationToken)
    {
        Private(); if (!Enabled) return NotFound();
        if (!ModelState.IsValid || screeningAnswer?.Length > 2000) return BadRequest();
        var result = await submission.PrepareAsync(Actor, jobId, screeningAnswer, cancellationToken);
        if (result.IsFailure) { TempData["ApplicationMessage"] = result.Error.Message; return RedirectToAction(nameof(Index)); }
        return View(new ApplicationReviewPage(jobId, screeningAnswer, result.Value));
    }

    [HttpPost("{jobId:guid}/submit"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.ApplicationWrite)]
    public async Task<IActionResult> Submit(Guid jobId, string? screeningAnswer, string? reviewToken, bool consent, CancellationToken cancellationToken)
    {
        Private(); if (!Enabled) return NotFound();
        if (!ModelState.IsValid || screeningAnswer?.Length > 2000 || reviewToken?.Length > 8192) return BadRequest();
        var result = await submission.SubmitAsync(new SubmitApplicationCommand(Actor, jobId, screeningAnswer, reviewToken ?? "", consent), cancellationToken);
        TempData["ApplicationMessage"] = result.IsSuccess ? "Application submitted." : result.Error.Message + " Please return to the job and review again.";
        return RedirectToAction(nameof(Index));
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
