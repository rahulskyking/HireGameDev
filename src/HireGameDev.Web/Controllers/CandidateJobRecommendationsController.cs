using System.Security.Claims;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Applications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.CandidateOnly)]
[Route("candidate/recommended-jobs")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CandidateJobRecommendationsController(IWebHostEnvironment environment, ICandidateJobRecommendationService? recommendations = null) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Vary = "Cookie";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (!ApplicationFeature.Enabled(environment) || recommendations is null) return NotFound();
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty) return Forbid();
        var result = await recommendations.GetAsync(actor, cancellationToken);
        return result.IsSuccess ? View(result.Value) : Forbid();
    }
}
