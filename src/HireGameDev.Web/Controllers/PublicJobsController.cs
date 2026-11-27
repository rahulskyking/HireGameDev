using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Constants;
using HireGameDev.Web.Models.Jobs;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class PublicJobsController(IPublicJobQueryService jobs, IReferenceDataService references,
    IPortalNavigator portals, ISavedJobService? savedJobs = null, IApplicationReviewService? applicationReview = null, IWebHostEnvironment? environment = null, IJobCardFitService? cardFits = null, ISavedJobStateQuery? savedStates = null, ICandidateJobApplicationStateQuery? applicationStates = null) : Controller
{
    [HttpGet("jobs")]
    public async Task<IActionResult> Index([FromQuery] PublicJobSearch search, CancellationToken cancellationToken)
    {
        var redirect = CanonicalPortal("/jobs" + Request.QueryString);
        if (redirect is not null) return redirect;
        PublicJobSearchPage? results = null;
        if (ModelState.IsValid)
        {
            var result = await jobs.SearchAsync(search, cancellationToken, viewerUserId: ViewerUserId());
            if (result.IsSuccess) results = result.Value;
            else ModelState.AddModelError("", result.Error.Message);
        }
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.Headers["X-Robots-Tag"] = "noindex, follow";
        }
        if (PublicJobUi.HasFilters(search) || (results?.Page ?? 1) > 1)
            Response.Headers["X-Robots-Tag"] = "noindex, follow";
        var canonicalPath = PublicJobUi.PageUrl(results is null ? new PublicJobSearch() : search, results?.Page ?? 1);
        var showMatches = environment?.IsDevelopment() == true && ViewerUserId().HasValue &&
            User.IsInRole(HireGameDevRoleNames.Candidate) && User.FindFirstValue(HireGameDevClaimTypes.AccountType) == HireGameDevRoleNames.Candidate;
        IReadOnlyDictionary<Guid, ApplicationFitScore>? fits = null;
        if (showMatches && cardFits is not null && results is not null && ViewerUserId() is Guid candidateId)
            fits = await cardFits.GetAsync(candidateId, results.Items.Select(x => x.Id).ToArray(), cancellationToken);
        IReadOnlySet<Guid>? savedIds = null;
        if (savedStates is not null && results is not null && ViewerUserId() is Guid savedUserId &&
            User.IsInRole(HireGameDevRoleNames.Candidate) && User.FindFirstValue(HireGameDevClaimTypes.AccountType) == HireGameDevRoleNames.Candidate)
        {
            var saved = await savedStates.GetSavedIdsAsync(savedUserId, results.Items.Select(x => x.Id).ToArray(), cancellationToken);
            if (saved.IsSuccess) savedIds = saved.Value;
        }
        IReadOnlyDictionary<Guid, CandidateJobApplicationState>? applications = null;
        if (showMatches && applicationStates is not null && results is not null && ViewerUserId() is Guid actor)
        {
            var states = await applicationStates.GetAsync(actor, results.Items.Select(x => x.Id).ToArray(), cancellationToken);
            if (states.IsSuccess) applications = states.Value;
        }
        return View(new PublicJobBrowsePage(search, results, await references.GetActiveCitiesAsync(cancellationToken),
            await references.GetActiveSkillsAsync(cancellationToken: cancellationToken),
            portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, canonicalPath), RegistrationUrl(canonicalPath), fits, showMatches, savedIds, applications));
    }

    [HttpGet("jobs/{slug}")]
    public async Task<IActionResult> Detail(string slug, CancellationToken cancellationToken)
    {
        var redirect = CanonicalPortal("/jobs/" + Uri.EscapeDataString(slug));
        if (redirect is not null) return redirect;
        var result = await jobs.GetBySlugAsync(slug, cancellationToken, viewerUserId: ViewerUserId());
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
        bool? isSaved = null;
        if (savedJobs is not null && ViewerUserId() is Guid userId && User.IsInRole(HireGameDevRoleNames.Candidate) &&
            User.FindFirstValue(HireGameDevClaimTypes.AccountType) == HireGameDevRoleNames.Candidate)
        {
            var state = await savedJobs.IsSavedAsync(userId, job.Summary.Id, cancellationToken);
            if (state.IsSuccess) isSaved = state.Value;
        }
        ApplicationFitScore? fit = null;
        var showFit = environment?.IsDevelopment() == true && ViewerUserId().HasValue &&
            User.IsInRole(HireGameDevRoleNames.Candidate) && User.FindFirstValue(HireGameDevClaimTypes.AccountType) == HireGameDevRoleNames.Candidate;
        if (showFit && applicationReview is not null && ViewerUserId() is Guid candidateId)
        {
            // Review service independently checks eligibility and returns a private, consistent preview.
            var review = await applicationReview.ReviewAsync(candidateId, job.Summary.Id, null, cancellationToken);
            if (review.IsSuccess) fit = ApplicationFitScore.Create(review.Value.Candidate, review.Value.Job);
        }
        CandidateJobApplicationState? application = null;
        if (showFit && applicationStates is not null && ViewerUserId() is Guid actor)
        {
            var states = await applicationStates.GetAsync(actor, new[] { job.Summary.Id }, cancellationToken);
            if (states.IsSuccess) states.Value.TryGetValue(job.Summary.Id, out application);
        }
        return View(new PublicJobDetailPage(job, portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, path), RegistrationUrl(path), isSaved, fit, showFit && fit is null && application is null, application));
    }

    private IActionResult? CanonicalPortal(string path)
    {
        var authenticated = User.Identity?.IsAuthenticated == true;
        Response.Headers["Cache-Control"] = authenticated ? "private, no-store" : "no-store";
        Response.Headers["Vary"] = "Cookie";
        if (authenticated) Response.Headers["X-Robots-Tag"] = "noindex, follow";
        return portals.IsSubdomainRoutingActive(HttpContext) && portals.ResolvePortal(HttpContext) != PortalSite.Jobs
            ? RedirectPermanent(portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, path)) : null;
    }
    private Guid? ViewerUserId() => User.Identity?.IsAuthenticated == true &&
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;

    private string RegistrationUrl(string returnPath) => portals.BuildPortalUrl(PortalSite.Jobs, HttpContext,
        "/Account/Register?type=candidate&returnUrl=" + Uri.EscapeDataString(returnPath));
}
