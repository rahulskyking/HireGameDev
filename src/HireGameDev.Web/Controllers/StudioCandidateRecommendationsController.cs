using System.Security.Claims;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Applications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.StudioMember)]
[Route("studio/recommended-candidates")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudioCandidateRecommendationsController(IWebHostEnvironment environment, IStudioCandidateRecommendationService? recommendations = null) : Controller
{
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Index(Guid jobId, Guid studioId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Vary = "Cookie";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (!ApplicationFeature.Enabled(environment) || recommendations is null) return NotFound();
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty) return Forbid();
        var result = await recommendations.GetAsync(actor, studioId, jobId, cancellationToken);
        return result.IsSuccess ? View(result.Value) : result.Error.Type == HireGameDev.Application.Common.ErrorType.NotFound ? NotFound() : StatusCode(503);
    }
}
