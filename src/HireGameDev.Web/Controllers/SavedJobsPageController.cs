using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.CandidateOnly)]
[Route("candidate/saved")]
public sealed class SavedJobsPageController(ISavedJobService service) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        Private();
        var result = await service.ListAsync(Actor(), page, 20, cancellationToken);
        if (result.IsFailure) return Forbid();
        return View(result.Value);
    }

    [HttpPost("{jobId:guid}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid jobId, string? slug, CancellationToken cancellationToken)
    {
        Private();
        var result = await service.SaveAsync(Actor(), jobId, cancellationToken);
        TempData["SavedJobMessage"] = result.IsSuccess ? "Job saved to your private list." : "This job could not be saved. It may no longer be available.";
        return SafeJobRedirect(slug);
    }

    [HttpPost("{jobId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid jobId, string? slug, CancellationToken cancellationToken)
    {
        Private();
        var result = await service.RemoveAsync(Actor(), jobId, cancellationToken);
        TempData["SavedJobMessage"] = result.IsSuccess ? "Job removed from your saved list." : "Your saved list could not be updated.";
        return string.IsNullOrEmpty(slug) ? RedirectToAction(nameof(Index)) : SafeJobRedirect(slug);
    }
    private IActionResult SafeJobRedirect(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= 160 && slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
            ? LocalRedirect("/jobs/" + slug) : RedirectToAction(nameof(Index));
    private Guid Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }
}
