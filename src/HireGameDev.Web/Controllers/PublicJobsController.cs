using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Models.Search;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Constants;
using HireGameDev.Web.Models.Jobs;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HireGameDev.Web.RateLimiting;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class PublicJobsController(IPublicJobQueryService jobs, IReferenceDataService references,
    IPortalNavigator portals, ISavedJobService? savedJobs = null, IApplicationReviewService? applicationReview = null, IWebHostEnvironment? environment = null, IJobCardFitService? cardFits = null, ISavedJobStateQuery? savedStates = null, ICandidateJobApplicationStateQuery? applicationStates = null, ISearchQuotaService? searchQuotas = null) : Controller
{
    [HttpGet("jobs")]
    public async Task<IActionResult> Index([FromQuery] PublicJobSearch search, CancellationToken cancellationToken, Guid? searchRun = null)
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
        var quotaCandidate = environment?.IsDevelopment() == true && searchQuotas is not null && ViewerUserId() is Guid &&
            User.IsInRole(HireGameDevRoleNames.Candidate) && User.FindFirstValue(HireGameDevClaimTypes.AccountType) == HireGameDevRoleNames.Candidate;
        if (quotaCandidate) { Response.Headers.CacheControl = "private, no-store"; Response.Headers.Vary = "Cookie"; Response.Headers["Referrer-Policy"] = "no-referrer"; }
        if (quotaCandidate && ModelState.IsValid && results is not null)
        {
            var check = await searchQuotas!.CheckAsync(ViewerUserId()!.Value, "candidate", null, SearchQuotaUi.Fingerprint(search), searchRun, cancellationToken);
            if (check.IsFailure) return Forbid();
            ViewData["SearchQuota"] = check.Value;
            ViewData["SearchQuotaEnabled"] = check.Value.Enabled;
            if (SearchQuotaUi.IsFiltered(search) && check.Value.Enabled && !check.Value.AuthorizedRun)
            {
                Response.Headers["Referrer-Policy"] = "no-referrer";
                return View("SearchQuotaConfirm", new SearchQuotaConfirmPage(Url.Action(nameof(StartSearch))!,
                    SearchQuotaUi.Fields(Request.Query, "Keyword", "StudioId", "Category", "JobType", "WorkMode", "CityId", "ExperienceYears", "SkillIds", "FresherFriendlyOnly", "Sort", "PageSize", "searchRun"), check.Value.Used, check.Value.Limit!.Value));
            }
        }
        if (quotaCandidate && searchQuotas is not null)
        {
            ViewData["SearchRun"] = searchRun;
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
        // Paid placements are disabled for the Free-only release; stored schedules are untouched.
        return View(new PublicJobBrowsePage(search, results, await references.GetActiveCitiesAsync(cancellationToken),
            await references.GetActiveSkillsAsync(cancellationToken: cancellationToken),
            portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, canonicalPath), RegistrationUrl(canonicalPath), fits, showMatches, savedIds, applications));
    }

    [Authorize(Policy = AuthorizationPolicies.CandidateOnly)]
    [HttpPost("jobs/search"), ValidateAntiForgeryToken, EnableRateLimiting(RateLimitPolicies.CandidateDiscoveryRead)]
    public async Task<IActionResult> StartSearch([FromForm] PublicJobSearch search, Guid? searchRun, Guid intentId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store"; Response.Headers["Referrer-Policy"] = "no-referrer";
        if (environment?.IsDevelopment() != true || searchQuotas is null || !ViewerUserId().HasValue) return NotFound();
        // Controller-level AllowAnonymous is needed for public browse and overrides action
        // authorization metadata, so explicitly require a Candidate here as well.
        if (!User.IsInRole(HireGameDevRoleNames.Candidate) || User.FindFirstValue(HireGameDevClaimTypes.AccountType) != HireGameDevRoleNames.Candidate) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        search = search with { Page = 1 };
        var validation = await jobs.SearchAsync(search, cancellationToken, ViewerUserId());
        if (validation.IsFailure) return BadRequest(validation.Error.Message);
        if (!SearchQuotaUi.IsFiltered(search)) return Redirect(PublicJobUi.PageUrl(search, 1));
        var started = await searchQuotas.StartAsync(ViewerUserId()!.Value, "candidate", null, SearchQuotaUi.Fingerprint(search), searchRun, intentId, cancellationToken);
        if (started.IsFailure)
        {
            if (started.Error.Type == HireGameDev.Application.Common.ErrorType.RateLimited)
            { Response.StatusCode = 429; return View("SearchQuotaReached", new SearchQuotaReachedPage(started.Error.Message, "/jobs")); }
            return started.Error.Type == HireGameDev.Application.Common.ErrorType.Conflict ? Conflict(started.Error.Message) : Forbid();
        }
        return Redirect(PublicJobUi.PageUrl(search, 1, started.Value.RunId));
    }

    [HttpGet("jobs/{slug}")]
    public async Task<IActionResult> Detail(string slug, CancellationToken cancellationToken, string? returnUrl = null)
    {
        var browseReturnUrl = PublicJobUi.IsSafeBrowseReturnUrl(returnUrl) ? returnUrl : null;
        var navigationQuery = browseReturnUrl is null ? "" : QueryString.Create("returnUrl", browseReturnUrl).ToString();
        if (browseReturnUrl is not null) Response.Headers["Referrer-Policy"] = "no-referrer";
        var redirect = CanonicalPortal("/jobs/" + Uri.EscapeDataString(slug) + navigationQuery);
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
            return RedirectPermanent(portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/jobs/" + job.Summary.Slug + navigationQuery));
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
        return View(new PublicJobDetailPage(job, portals.BuildPortalUrl(PortalSite.Jobs, HttpContext, path), RegistrationUrl(path), isSaved, fit, showFit && fit is null && application is null, application, browseReturnUrl));
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
