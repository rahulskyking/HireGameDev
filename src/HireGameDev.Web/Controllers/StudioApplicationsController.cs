using System.Security.Claims;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Applications;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.StudioMember)]
[Route("studio/applications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudioApplicationsController(IStudioApplicantListService list, IStudioApplicantDetailService detail,
    IStudioApplicationStatusService status, IWebHostEnvironment environment) : Controller
{
    private bool Enabled => ApplicationFeature.Enabled(environment);
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }

    [HttpGet("job/{jobId:guid}")]
    public async Task<IActionResult> Index(Guid jobId, int page = 1, ApplicationStatus? applicationStatus = null, CancellationToken cancellationToken = default, Guid? studioId = null)
    {
        Private(); if (!Enabled) return NotFound();
        if (!ModelState.IsValid) return BadRequest();
        var result = await list.ListAsync(Actor, jobId, page, 25, cancellationToken, applicationStatus);
        if (result.IsFailure) return BadRequest();
        return View(new ApplicantListPage(jobId, studioId, page, applicationStatus, result.Value));
    }

    [HttpGet("{applicationId:guid}")]
    public async Task<IActionResult> Detail(Guid applicationId, CancellationToken cancellationToken, Guid? studioId = null, int page = 1, ApplicationStatus? applicationStatus = null)
    {
        Private(); if (!Enabled) return NotFound();
        var result = await detail.GetAsync(Actor, applicationId, cancellationToken);
        if (result.IsFailure) return NotFound();
        return View(new StudioApplicantDetailPage(result.Value, studioId, SafePage(page), SafeStatus(applicationStatus)));
    }

    private static int SafePage(int page) => page is >= 1 and <= 1000000 ? page : 1;
    private static ApplicationStatus? SafeStatus(ApplicationStatus? status) =>
        status.HasValue && Enum.IsDefined(status.Value) && status.Value != ApplicationStatus.Withdrawn ? status : null;

    [HttpPost("{applicationId:guid}/status"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.ApplicationWrite)]
    public async Task<IActionResult> ChangeStatus(Guid applicationId, ApplicationStatus newStatus, CancellationToken cancellationToken, bool confirmed = false, Guid? studioId = null, int page = 1, ApplicationStatus? applicationStatus = null)
    {
        Private(); if (!Enabled) return NotFound();
        if (!ModelState.IsValid || !Enum.IsDefined(newStatus) || !confirmed) return BadRequest();
        var result = await status.ChangeStatusAsync(Actor, applicationId, newStatus, cancellationToken);
        TempData["ApplicationMessage"] = result.IsSuccess ? "Status updated." : "Status could not be updated. Access or application status may have changed.";
        return RedirectToAction(nameof(Detail), new { applicationId, studioId, page = SafePage(page), applicationStatus = SafeStatus(applicationStatus) });
    }
}
