using System.Security.Claims;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[Route("Media")]
public sealed class MediaController : Controller
{
    private readonly ICandidatePhotoService _candidatePhotoService;
    private readonly IFileStorageService _fileStorageService;

    public MediaController(
        ICandidatePhotoService candidatePhotoService,
        IFileStorageService fileStorageService)
    {
        _candidatePhotoService = candidatePhotoService;
        _fileStorageService = fileStorageService;
    }

    [AllowAnonymous]
    [HttpGet("Candidates/{candidateId:guid}/Avatar")]
    public async Task<IActionResult> CandidateAvatar(
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var currentUserId = isAuthenticated &&
                            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId) &&
                            parsedId != Guid.Empty
            ? parsedId
            : (Guid?)null;

        var viewer = new CandidateMediaViewer(
            IsAuthenticated: isAuthenticated,
            UserId: currentUserId,
            IsAdmin: isAuthenticated && User.IsInRole(HireGameDevRoleNames.Admin),
            IsStudio: isAuthenticated && User.IsInRole(HireGameDevRoleNames.Studio));

        var result = await _candidatePhotoService.OpenAuthorizedAvatarAsync(
            candidateId,
            viewer,
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.Type switch
            {
                ErrorType.Unauthorized => Challenge(),
                ErrorType.Forbidden => Forbid(),
                _ => NotFound()
            };
        }

        var readResult = result.Value;

        Response.Headers["Cache-Control"] = "private, max-age=3600";
        Response.Headers["Vary"] = "Cookie";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (!string.IsNullOrWhiteSpace(readResult.ETag))
        {
            Response.Headers["ETag"] = readResult.ETag;

            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrWhiteSpace(ifNoneMatch) &&
                string.Equals(ifNoneMatch.Trim(), readResult.ETag, StringComparison.Ordinal))
            {
                await readResult.DisposeAsync();
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        return File(readResult.Content, readResult.ContentType);
    }

    /// <summary>
    /// Serves <see cref="StorageVisibility.Public"/> assets (such as verified studio logos in M1.9)
    /// when <c>Storage:Provider = Local</c>. Strictly isolated from <see cref="StorageVisibility.Private"/> candidate objects.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("Public/{*objectKey}")]
    public async Task<IActionResult> PublicAsset(
        string? objectKey,
        CancellationToken cancellationToken)
    {
        if (!ObjectKeyRules.TryNormalize(objectKey, out var normalizedKey, out _))
        {
            return NotFound();
        }

        var readResult = await _fileStorageService.OpenReadAsync(
            normalizedKey,
            StorageVisibility.Public,
            cancellationToken);

        if (readResult is null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "public, max-age=86400, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        if (!string.IsNullOrWhiteSpace(readResult.ETag))
        {
            Response.Headers["ETag"] = readResult.ETag;

            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrWhiteSpace(ifNoneMatch) &&
                string.Equals(ifNoneMatch.Trim(), readResult.ETag, StringComparison.Ordinal))
            {
                await readResult.DisposeAsync();
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        return File(readResult.Content, readResult.ContentType);
    }
}
