using System.Security.Claims;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Constants;
using HireGameDev.Web.Models.Studio;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

/// <summary>
/// Public SEO directory endpoints hosted canonically on <c>jobs.hiregamedev.com</c>.
/// </summary>
[AllowAnonymous]
public sealed class PublicDirectoryController : Controller
{
    private readonly IPortalNavigator _portalNavigator;
    private readonly IStudioProfileService _studioProfileService;

    public PublicDirectoryController(
        IPortalNavigator portalNavigator,
        IStudioProfileService studioProfileService)
    {
        _portalNavigator = portalNavigator;
        _studioProfileService = studioProfileService;
    }

    [HttpGet("studios/{slug}")]
    public async Task<IActionResult> StudioProfile(
        string slug,
        string? preview = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return RedirectToAction("Index", "Home");
        }

        var isPreviewQuery = string.Equals(preview, "1", StringComparison.Ordinal) ||
                             string.Equals(preview, "true", StringComparison.OrdinalIgnoreCase);

        var portal = _portalNavigator.ResolvePortal(HttpContext);

        // Canonicalize public /studios/{slug} requests onto jobs.hiregamedev.com when subdomain routing is active.
        if (portal is PortalSite.Landing or PortalSite.Studio &&
            _portalNavigator.IsSubdomainRoutingActive(HttpContext))
        {
            var previewSuffix = isPreviewQuery ? "?preview=true" : string.Empty;
            var canonical = _portalNavigator.BuildPortalUrl(
                PortalSite.Jobs,
                HttpContext,
                $"/studios/{Uri.EscapeDataString(slug.Trim().ToLowerInvariant())}{previewSuffix}");

            return RedirectPermanent(canonical);
        }

        Guid? viewerUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId) &&
                             parsedId != Guid.Empty
            ? parsedId
            : null;

        var viewer = new PublicStudioViewer(
            IsAuthenticated: User.Identity?.IsAuthenticated == true && viewerUserId.HasValue,
            UserId: viewerUserId,
            IsAdmin: User.IsInRole(HireGameDevRoleNames.Admin));

        var result = await _studioProfileService.GetPublicStudioBySlugAsync(slug, viewer, cancellationToken);
        if (result.IsFailure)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            ViewData["StudioSlug"] = slug.Trim().ToLowerInvariant();
            return View("StudioNotFound");
        }

        var studio = result.Value;
        if (!string.Equals(slug, studio.Slug, StringComparison.Ordinal))
        {
            if (isPreviewQuery)
            {
                return RedirectToAction(nameof(StudioProfile), new { slug = studio.Slug, preview = "true" });
            }

            return RedirectToAction(nameof(StudioProfile), new { slug = studio.Slug });
        }

        var isExplicitMemberOrAdminPreview = isPreviewQuery &&
                                             (studio.ViewerMemberRole.HasValue || viewer.IsAdmin);

        if (studio.IsPreviewMode || isPreviewQuery)
        {
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        }

        if (studio.IsPreviewMode || isExplicitMemberOrAdminPreview)
        {
            Response.Headers["Cache-Control"] = "private, no-store";
        }

        var canonicalPublicUrl = _portalNavigator.BuildPortalUrl(
            PortalSite.Jobs,
            HttpContext,
            $"/studios/{studio.Slug}");

        return View("StudioProfile", new PublicStudioPageViewModel
        {
            Studio = studio,
            CanonicalPublicUrl = canonicalPublicUrl,
            IsExplicitPreviewRequest = isExplicitMemberOrAdminPreview
        });
    }
}
