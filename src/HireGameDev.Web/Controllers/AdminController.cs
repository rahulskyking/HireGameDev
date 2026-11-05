using HireGameDev.Web.Authorization;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

/// <summary>
/// ADR-012 Decision #3: Admin panel is served strictly on hiregamedev.com/Admin.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[Route("Admin")]
public sealed class AdminController : Controller
{
    private readonly IPortalNavigator _portalNavigator;

    public AdminController(IPortalNavigator portalNavigator)
    {
        _portalNavigator = portalNavigator;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        if (_portalNavigator.ResolvePortal(HttpContext) != PortalSite.Landing)
        {
            return NotFound();
        }

        return View();
    }
}
