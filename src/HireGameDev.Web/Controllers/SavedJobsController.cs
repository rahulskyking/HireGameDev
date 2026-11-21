using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Common;
using HireGameDev.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

// Backend endpoints only. UI/authentication return flow is a separate slice.
[Authorize(Policy = AuthorizationPolicies.CandidateOnly)]
[Route("candidate/saved-jobs")]
public sealed class SavedJobsController(ISavedJobService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        NoCache();
        var result = await service.ListAsync(Actor(), page, pageSize, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error);
    }

    [HttpPost("{jobId:guid}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid jobId, CancellationToken cancellationToken)
    {
        NoCache();
        var result = await service.SaveAsync(Actor(), jobId, cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error);
    }

    [HttpPost("{jobId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid jobId, CancellationToken cancellationToken)
    {
        NoCache();
        var result = await service.RemoveAsync(Actor(), jobId, cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error);
    }

    private Guid Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private void NoCache() { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }
    private IActionResult Failure(Error error) => error.Type switch
    {
        ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
        ErrorType.NotFound => NotFound(),
        _ => BadRequest(new { error = error.Message })
    };
}
