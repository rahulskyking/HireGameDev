using System.Security.Claims;
using HireGameDev.Application.Abstractions.Discovery;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Applications;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.StudioMember)]
[EnableRateLimiting(RateLimitPolicies.CandidateDiscoveryRead)]
[Route("studio/candidates")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudioCandidatesController(IWebHostEnvironment environment, IStudioProfileService studios,
    IReferenceDataService references, IStudioCandidateDiscoveryService? discovery = null) : Controller
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void Private() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["Referrer-Policy"] = "no-referrer"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? studioId, [FromQuery] CandidateDiscoverySearch search, CancellationToken ct)
    {
        Private(); if (!ApplicationFeature.Enabled(environment) || discovery is null) return NotFound();
        if (Actor == Guid.Empty) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        var memberships = await studios.GetUserMembershipsAsync(Actor, ct);
        if (memberships.Count == 0) return RedirectToAction("Onboarding", "Studio");
        var studio = studioId.HasValue ? memberships.FirstOrDefault(m => m.StudioId == studioId) : memberships[0];
        if (studio is null) return Forbid();
        var result = await discovery.SearchAsync(Actor, studio.StudioId, search, ct);
        if (result.IsFailure && result.Error.Type != ErrorType.Forbidden) return BadRequest(result.Error.Message);
        return View(new StudioCandidateDiscoveryViewModel(studio, memberships, search, result.IsSuccess ? result.Value : null,
            await references.GetActiveCitiesAsync(ct), await references.GetActiveSkillsAsync(cancellationToken: ct)));
    }

    [HttpGet("{candidateId:guid}")]
    public async Task<IActionResult> Detail(Guid candidateId, Guid studioId, string? returnUrl, CancellationToken ct)
    {
        Private(); if (!ApplicationFeature.Enabled(environment) || discovery is null) return NotFound();
        if (Actor == Guid.Empty) return Forbid();
        var result = await discovery.GetAsync(Actor, studioId, candidateId, ct);
        if (result.IsFailure) return NotFound();
        // Return navigation is not an authorization mechanism.
        var safeReturn = returnUrl is not null && returnUrl.Length <= 2048 &&
            (returnUrl.Equals("/studio/candidates", StringComparison.OrdinalIgnoreCase) || returnUrl.StartsWith("/studio/candidates?", StringComparison.OrdinalIgnoreCase)) &&
            Url.IsLocalUrl(returnUrl) && !returnUrl.Contains('\\') && !returnUrl.Any(char.IsControl) ? returnUrl : null;
        return View(new StudioCandidateDiscoveryDetailViewModel(studioId, result.Value, safeReturn));
    }
}

public sealed record StudioCandidateDiscoveryViewModel(StudioMembershipSummary Studio, IReadOnlyList<StudioMembershipSummary> Memberships,
    CandidateDiscoverySearch Search, CandidateDiscoveryPage? Results, IReadOnlyList<CityLookupItem> Cities, IReadOnlyList<SkillLookupItem> Skills);
public sealed record StudioCandidateDiscoveryDetailViewModel(Guid StudioId, CandidateDiscoveryDetail Candidate, string? ReturnUrl);
