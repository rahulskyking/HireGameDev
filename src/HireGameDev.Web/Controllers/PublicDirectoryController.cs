using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class PublicDirectoryController : Controller
{
    private readonly IPortalNavigator _portalNavigator;

    public PublicDirectoryController(IPortalNavigator portalNavigator)
    {
        _portalNavigator = portalNavigator;
    }

    /// <summary>
    /// ADR-012 Decision #2: Public studio profiles live canonically on jobs.hiregamedev.com/studios/{slug}.
    /// </summary>
    [HttpGet("studios/{slug}")]
    public IActionResult StudioProfile(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return NotFound();
        }

        var portal = _portalNavigator.ResolvePortal(HttpContext);
        if (portal != PortalSite.Jobs && _portalNavigator.DeploymentMode == PortalDeploymentMode.All)
        {
            var canonical = _portalNavigator.BuildPortalUrl(
                PortalSite.Jobs,
                HttpContext,
                $"/studios/{Uri.EscapeDataString(slug.Trim())}");
            return RedirectPermanent(canonical);
        }

        ViewData["StudioSlug"] = slug.Trim().ToLowerInvariant();
        return View();
    }

    [HttpGet("jobs/{slug}")]
    public IActionResult JobDetail(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return NotFound();
        }

        var portal = _portalNavigator.ResolvePortal(HttpContext);
        if (portal != PortalSite.Jobs && _portalNavigator.DeploymentMode == PortalDeploymentMode.All)
        {
            var canonical = _portalNavigator.BuildPortalUrl(
                PortalSite.Jobs,
                HttpContext,
                $"/jobs/{Uri.EscapeDataString(slug.Trim())}");
            return RedirectPermanent(canonical);
        }

        ViewData["JobSlug"] = slug.Trim().ToLowerInvariant();
        return View();
    }
}
