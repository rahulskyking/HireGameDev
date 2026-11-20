using System.Reflection;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Enums;
using HireGameDev.Web.Controllers;
using HireGameDev.Web.Models.Jobs;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HireGameDev.UnitTests.Web;

public sealed class PublicJobsControllerTests
{
    [Fact]
    public async Task AnonymousBrowse_UsesQueryAndDoesNotRequireRegistration()
    {
        var h = new Harness();
        var search = new PublicJobSearch { Keyword = "Unity", SkillIds = new[] { 1, 2 }, Page = 2 };
        var result = await h.Controller.Index(search, default);
        var page = Assert.IsType<PublicJobBrowsePage>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(search, h.Jobs.LastSearch);
        Assert.NotNull(page.Results);
        Assert.Contains("/Account/Register?type=candidate", page.RegistrationUrl);
        Assert.Contains("returnUrl=", page.RegistrationUrl);
        Assert.Equal("no-store", h.Controller.Response.Headers["Cache-Control"].ToString());
        Assert.NotNull(typeof(PublicJobsController).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task InvalidBinding_DoesNotQueryAndReturnsUsefulErrorPage()
    {
        var h = new Harness();
        h.Controller.ModelState.AddModelError("ExperienceYears", "Enter a number.");
        var view = Assert.IsType<ViewResult>(await h.Controller.Index(new(), default));
        Assert.Null(Assert.IsType<PublicJobBrowsePage>(view.Model).Results);
        Assert.Null(h.Jobs.LastSearch);
        Assert.Equal(400, h.Controller.Response.StatusCode);
    }

    [Fact]
    public async Task InvalidSearch_Returns400WithoutClaimingZeroMatches()
    {
        var h = new Harness(); h.Jobs.SearchError = Error.Validation("PublicJobs.InvalidSearch", "Invalid filters.");
        var view = Assert.IsType<ViewResult>(await h.Controller.Index(new(), default));
        Assert.Null(Assert.IsType<PublicJobBrowsePage>(view.Model).Results);
        Assert.False(h.Controller.ModelState.IsValid);
        Assert.Equal(400, h.Controller.Response.StatusCode);
    }

    [Fact]
    public async Task HiddenOrMissingDetail_ReturnsNeutral404AndNoIndex()
    {
        var h = new Harness(); h.Jobs.Missing = true;
        var view = Assert.IsType<ViewResult>(await h.Controller.Detail("hidden-draft", default));
        Assert.Equal("Unavailable", view.ViewName);
        Assert.Null(view.Model);
        Assert.Equal(404, h.Controller.Response.StatusCode);
        Assert.Equal("noindex, nofollow", h.Controller.Response.Headers["X-Robots-Tag"].ToString());
    }

    [Fact]
    public async Task VisibleDetail_UsesPublicDtoAndCanonicalUrl()
    {
        var h = new Harness();
        var view = Assert.IsType<ViewResult>(await h.Controller.Detail("job-test", default));
        var page = Assert.IsType<PublicJobDetailPage>(view.Model);
        Assert.Equal(h.Jobs.Detail, page.Job);
        Assert.Equal("https://localhost:7140/jobs/job-test", page.CanonicalUrl);
        Assert.Equal("no-store", h.Controller.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public async Task WrongPortal_RedirectsBeforeReadingCatalog_AndPreservesSearch()
    {
        var h = new Harness(); h.Portals.Subdomains = true; h.Portals.Site = PortalSite.Landing;
        h.Controller.Request.QueryString = new QueryString("?Keyword=Unity&SkillIds=1&SkillIds=2");
        var redirect = Assert.IsType<RedirectResult>(await h.Controller.Index(new(), default));
        Assert.True(redirect.Permanent);
        Assert.Equal("https://jobs.hiregamedev.com/jobs?Keyword=Unity&SkillIds=1&SkillIds=2", redirect.Url);
        Assert.Null(h.Jobs.LastSearch);
    }

    [Fact]
    public async Task SlugCanonicalization_HappensOnlyAfterVisibleLookup()
    {
        var h = new Harness();
        var redirect = Assert.IsType<RedirectResult>(await h.Controller.Detail("JOB-TEST", default));
        Assert.Equal("https://localhost:7140/jobs/job-test", redirect.Url);
        Assert.True(redirect.Permanent);
    }

    [Fact]
    public void Pagination_PreservesEveryFilterAndEncodesValuesWithoutOpenRedirects()
    {
        var search = new PublicJobSearch { Keyword = "C++ & Unity", Category = DisciplineCategory.Programming,
            JobType = JobType.Contract, WorkMode = JobWorkMode.Hybrid, CityId = 1, ExperienceYears = 1.5m,
            SkillIds = new[] { 1, 2 }, FresherFriendlyOnly = true, PageSize = 10 };
        var path = PublicJobUi.PageUrl(search, 3);
        var parsed = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(path[(path.IndexOf('?'))..]);
        Assert.StartsWith("/jobs?", path);
        Assert.Equal("C++ & Unity", parsed["Keyword"].ToString());
        Assert.Equal("1.5", parsed["ExperienceYears"].ToString());
        Assert.Equal(new[] { "1", "2" }, parsed["SkillIds"].ToArray());
        foreach (var key in new[] { "Category", "JobType", "WorkMode", "CityId", "FresherFriendlyOnly", "PageSize", "Page" })
            Assert.True(parsed.ContainsKey(key));
    }

    private sealed class Harness
    {
        public FakeCatalog Jobs { get; } = new();
        public FakePortals Portals { get; } = new();
        public PublicJobsController Controller { get; }
        public Harness()
        {
            var context = new DefaultHttpContext(); context.Request.Scheme = "https"; context.Request.Host = new HostString("localhost", 7140);
            Controller = new(Jobs, DispatchProxy.Create<IReferenceDataService, StudioJobsControllerTests.LookupProxy>(), Portals)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        }
    }
    private sealed class FakeCatalog : IPublicJobQueryService
    {
        public PublicJobSearch? LastSearch { get; private set; }
        public Error? SearchError { get; set; }
        public bool Missing { get; set; }
        public PublicJobDetail Detail { get; } = new(
            new PublicJobCard(Guid.NewGuid(), "job-test", "Unity Developer", new(Guid.NewGuid(), "Studio", "studio", null),
                JobType.FullTime, JobWorkMode.Remote, null, null, 10, 15, JobPayUnit.Lpa, 1, 4, false,
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29)),
            DisciplineCategory.Programming, SeniorityLevel.Junior, SeniorityLevel.Mid, "Build games.", false, 1, null, null, Array.Empty<PublicJobSkill>());
        public Task<Result<PublicJobSearchPage>> SearchAsync(PublicJobSearch search, CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            return Task.FromResult(SearchError is null ? Result.Success(new PublicJobSearchPage(new[] { Detail.Summary }, 1, 1, 20, 1)) : Result.Failure<PublicJobSearchPage>(SearchError));
        }
        public Task<Result<PublicJobDetail>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Missing ? Result.Failure<PublicJobDetail>(Error.NotFound("PublicJobs.Unavailable", "This listing is unavailable.")) : Result.Success(Detail));
    }
    private sealed class FakePortals : IPortalNavigator
    {
        public bool Subdomains { get; set; }
        public PortalSite Site { get; set; } = PortalSite.Landing;
        public PortalDeploymentMode DeploymentMode => PortalDeploymentMode.All;
        public PortalSite ResolvePortal(HttpContext context) => Site;
        public bool IsSubdomainRoutingActive(HttpContext context) => Subdomains;
        public bool IsPortalServedByThisDeployment(PortalSite site) => true;
        public string GetPortalBaseUrl(PortalSite site, HttpContext context) => Subdomains ? "https://jobs.hiregamedev.com" : "https://localhost:7140";
        public string BuildPortalUrl(PortalSite site, HttpContext context, string pathAndQuery = "/") => GetPortalBaseUrl(site, context) + pathAndQuery;
        public bool IsSafeReturnUrl(string? url, HttpContext context) => true;
        public string? ResolveSharedCookieDomain(HttpContext context) => null;
        public bool TryBuildWwwCanonicalRedirect(HttpContext context, out string url) { url = ""; return false; }
    }
}
