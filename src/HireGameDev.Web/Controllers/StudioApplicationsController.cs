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
    IStudioApplicationStatusService status, IWebHostEnvironment environment, IConfiguration configuration) : Controller
{
    private bool Enabled => ApplicationPilot.Enabled(environment, configuration);
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }

    [HttpGet("job/{jobId:guid}")]
    public async Task<IActionResult> Index(Guid jobId, int page = 1, CancellationToken cancellationToken = default)
    {
        Private(); if (!Enabled) return NotFound();
        if (!ModelState.IsValid) return BadRequest();
        var result = await list.ListAsync(Actor, jobId, page, 25, cancellationToken);
        if (result.IsFailure) return BadRequest();
        return View(new ApplicantListPage(jobId, page, result.Value));
    }

    [HttpGet("{applicationId:guid}")]
    public async Task<IActionResult> Detail(Guid applicationId, CancellationToken cancellationToken)
    {
        Private(); if (!Enabled) return NotFound();
        var result = await detail.GetAsync(Actor, applicationId, cancellationToken);
        return result.IsSuccess ? View(result.Value) : NotFound();
    }

    [HttpPost("{applicationId:guid}/status"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.ApplicationWrite)]
    public async Task<IActionResult> ChangeStatus(Guid applicationId, ApplicationStatus newStatus, CancellationToken cancellationToken)
    {
        Private(); if (!Enabled) return NotFound();
        if (!ModelState.IsValid || !Enum.IsDefined(newStatus)) return BadRequest();
        var result = await status.ChangeStatusAsync(Actor, applicationId, newStatus, cancellationToken);
        TempData["ApplicationMessage"] = result.IsSuccess ? "Status updated." : "Status could not be updated. Access or application status may have changed.";
        return RedirectToAction(nameof(Detail), new { applicationId });
    }
}
