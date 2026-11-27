using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.CandidateOnly)]
[Route("candidate/saved")]
public sealed class SavedJobsPageController(ISavedJobService service, IJobCardFitService? cardFits = null, IWebHostEnvironment? environment = null, ICandidateJobApplicationStateQuery? applicationStates = null) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        Private();
        var result = await service.ListAsync(Actor(), page, 20, cancellationToken);
        if (result.IsFailure) return Forbid();
        if (environment?.IsDevelopment() == true && cardFits is not null)
        {
            var visibleIds = result.Value.Items.Where(x => x.Job is not null).Select(x => x.JobId).ToArray();
            ViewData["FitScores"] = await cardFits.GetAsync(Actor(), visibleIds, cancellationToken);
            ViewData["ShowMatches"] = true;
            if (applicationStates is not null)
            {
                var states = await applicationStates.GetAsync(Actor(), visibleIds, cancellationToken);
                if (states.IsSuccess) ViewData["ApplicationStates"] = states.Value;
            }
        }
        return View(result.Value);
    }

    [HttpPost("{jobId:guid}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid jobId, string? slug, CancellationToken cancellationToken, string? returnUrl = null)
    {
        Private();
        var result = await service.SaveAsync(Actor(), jobId, cancellationToken);
        TempData["SavedJobMessage"] = result.IsSuccess ? "Job saved to your private list." : "This job could not be saved. It may no longer be available.";
        return SafeBrowseRedirect(returnUrl) ?? SafeJobRedirect(slug);
    }

    [HttpPost("{jobId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid jobId, string? slug, CancellationToken cancellationToken, string? returnUrl = null, int? page = null)
    {
        Private();
        var result = await service.RemoveAsync(Actor(), jobId, cancellationToken);
        TempData["SavedJobMessage"] = result.IsSuccess ? "Job removed from your saved list." : "Your saved list could not be updated.";
        return SafeBrowseRedirect(returnUrl) ?? (string.IsNullOrEmpty(slug)
            ? RedirectToAction(nameof(Index), new { page = page is > 0 and <= 100000 ? page : 1 })
            : SafeJobRedirect(slug));
    }
    // Only return to the jobs listing, never an arbitrary supplied URL.
    private IActionResult? SafeBrowseRedirect(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.Length <= 4096 &&
        (returnUrl == "/jobs" || returnUrl.StartsWith("/jobs?", StringComparison.Ordinal) || returnUrl.StartsWith("/jobs#", StringComparison.Ordinal)) &&
        !returnUrl.Contains('\\') && !returnUrl.Any(char.IsControl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl) : null;
    private IActionResult SafeJobRedirect(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= 160 && slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
            ? LocalRedirect("/jobs/" + slug) : RedirectToAction(nameof(Index));
    private Guid Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }
}
