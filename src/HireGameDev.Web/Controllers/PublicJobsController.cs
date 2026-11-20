using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Web.Models.Jobs;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicJobsController(IPublicJobQueryService jobs, IReferenceDataService references,
    IPortalNavigator portals) : Controller
{
    [HttpGet("jobs")]
    public async Task<IActionResult> Index([FromQuery] PublicJobSearch search, CancellationToken cancellationToken)
    {
        var redirect = CanonicalPortal("/jobs" + Request.QueryString);
        if (redirect is not null) return redirect;
        PublicJobSearchPage? results = null;
        if (ModelState.IsValid)
        {
            var result = await jobs.SearchAsync(search, cancellationToken);
            if (result.IsSuccess) results = result.Value;
            else ModelState.AddModelError("", result.Error.Message);
        }
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.Headers["X-Robots-Tag"] = "noindex, follow";
        }
        var canonicalPath = PublicJobUi.PageUrl(results is null ? new PublicJobSearch() : search, results?.Page ?? 1);
        return View(new PublicJobBrowsePage(search, results, await references.GetActiveCitiesAsync(cancellationToken),
            await references.GetActiveSkillsAsync(cancellationToken: cancellationToken),
            portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, canonicalPath), RegistrationUrl(canonicalPath)));
    }

    [HttpGet("jobs/{slug}")]
    public async Task<IActionResult> Detail(string slug, CancellationToken cancellationToken)
    {
        var redirect = CanonicalPortal("/jobs/" + Uri.EscapeDataString(slug));
        if (redirect is not null) return redirect;
        var result = await jobs.GetBySlugAsync(slug, cancellationToken);
        if (result.IsFailure)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            return View("Unavailable");
        }
        var job = result.Value;
        if (!string.Equals(slug, job.Summary.Slug, StringComparison.Ordinal))
            return RedirectPermanent(portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/jobs/" + job.Summary.Slug));
        var path = "/jobs/" + job.Summary.Slug;
        return View(new PublicJobDetailPage(job, portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, path), RegistrationUrl(path)));
    }

    private IActionResult? CanonicalPortal(string path)
    {
        Response.Headers["Cache-Control"] = "no-store"; // Re-evaluate expiry/restrictions on every request.
        return portals.IsSubdomainRoutingActive(HttpContext) && portals.ResolvePortal(HttpContext) != PortalSite.Jobs
            ? RedirectPermanent(portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, path)) : null;
    }
    private string RegistrationUrl(string returnPath) => portals.BuildPortalUrl(PortalSite.Jobs, HttpContext,
        "/Account/Register?type=candidate&returnUrl=" + Uri.EscapeDataString(returnPath));
}
