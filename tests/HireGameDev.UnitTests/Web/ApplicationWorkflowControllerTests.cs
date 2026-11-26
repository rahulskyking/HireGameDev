using System.Reflection;
using System.Security.Claims;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace HireGameDev.UnitTests.Web;

public sealed class ApplicationWorkflowControllerTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task ApplicationsAreDisabledOutsideDevelopment(string environment)
    {
        var fake = new FakeServices();
        var controller = Candidate(fake, Guid.NewGuid(), environment);
        Assert.IsType<NotFoundResult>(await controller.Review(Guid.NewGuid(), null, default));
        Assert.Equal(0, fake.Calls);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", controller.Response.Headers["Referrer-Policy"].ToString());
        var studio = new StudioApplicationsController(fake, fake, fake, Environment(environment));
        Attach(studio, Guid.NewGuid());
        Assert.IsType<NotFoundResult>(await studio.ChangeStatus(Guid.NewGuid(), ApplicationStatus.Reviewing, default));
        Assert.Equal(0, fake.Calls);
        Assert.Equal("no-referrer", studio.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task CandidateSubmitAndWithdrawUseAuthenticatedActor()
    {
        var fake = new FakeServices(); var actor = Guid.NewGuid(); var job = Guid.NewGuid(); var app = Guid.NewGuid();
        var controller = Candidate(fake, actor, "Development");
        Assert.IsType<RedirectToActionResult>(await controller.Submit(job, "answer", "token", true, default));
        Assert.Equal(actor, fake.Command!.ActorUserId); Assert.Equal(job, fake.Command.JobId);
        Assert.True(fake.Command.Consent); Assert.Equal("token", fake.Command.ReviewToken);
        Assert.IsType<RedirectToActionResult>(await controller.Withdraw(app, default));
        Assert.Equal(actor, fake.Actor); Assert.Equal(app, fake.Application);
    }

    [Fact]
    public async Task ReviewRetryUsesSafeNavigationAndPreservesMultilineAnswerOnSubmit()
    {
        var fake = new FakeServices(); var actor = Guid.NewGuid(); var job = Guid.NewGuid();
        var controller = Candidate(fake, actor, "Development");
        var failure = Assert.IsType<ViewResult>(await controller.Review(job, "first\nsecond", default, "//example.invalid"));
        var retry = Assert.IsType<HireGameDev.Web.Models.Applications.ApplicationRetryPage>(failure.Model);
        Assert.Null(retry.JobSlug); Assert.Equal("first\nsecond", retry.ScreeningAnswer);
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("first\r\nsecond"));
        Assert.IsType<RedirectToActionResult>(await controller.Submit(job, null, "token", true, default, "safe-job", encoded));
        Assert.Equal("first\r\nsecond", fake.Command!.ScreeningAnswer);
        var calls = fake.Calls;
        Assert.IsType<BadRequestResult>(await controller.Submit(job, null, "token", true, default, null, "not-base64"));
        Assert.Equal(calls, fake.Calls);
    }

    [Fact]
    public async Task StudioStatusUsesAuthenticatedActorAndRejectsInvalidStatus()
    {
        var fake = new FakeServices(); var actor = Guid.NewGuid(); var app = Guid.NewGuid();
        var controller = new StudioApplicationsController(fake, fake, fake, Environment("Development"));
        Attach(controller, actor);
        Assert.IsType<BadRequestResult>(await controller.ChangeStatus(app, (ApplicationStatus)999, default));
        Assert.Equal(0, fake.Calls);
        var workspace = Guid.NewGuid();
        var redirect = Assert.IsType<RedirectToActionResult>(await controller.ChangeStatus(app, ApplicationStatus.Reviewing, default,
            confirmed: true, studioId: workspace, page: 2, applicationStatus: ApplicationStatus.Submitted));
        Assert.Equal(actor, fake.Actor); Assert.Equal(app, fake.Application);
        Assert.Equal(workspace, redirect.RouteValues!["studioId"]);
        Assert.Equal(2, redirect.RouteValues["page"]);
        Assert.Equal(ApplicationStatus.Submitted, redirect.RouteValues["applicationStatus"]);
    }

    [Fact]
    public void ControllersRequireRolePoliciesAndCsrfOnMutations()
    {
        Assert.Equal(AuthorizationPolicies.CandidateOnly, typeof(CandidateApplicationsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(AuthorizationPolicies.StudioMember, typeof(StudioApplicationsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        foreach (var method in new[] { nameof(CandidateApplicationsController.Review), nameof(CandidateApplicationsController.Submit), nameof(CandidateApplicationsController.Withdraw) })
            Assert.NotNull(typeof(CandidateApplicationsController).GetMethod(method)!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(typeof(StudioApplicationsController).GetMethod(nameof(StudioApplicationsController.ChangeStatus))!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private static CandidateApplicationsController Candidate(FakeServices fake, Guid actor, string environment)
    {
        var controller = new CandidateApplicationsController(fake, fake, fake, fake, Environment(environment));
        Attach(controller, actor); return controller;
    }
    private static void Attach(Controller controller, Guid actor)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, actor.ToString()) }, "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, new EmptyTempData());
    }
    private static IWebHostEnvironment Environment(string name) => new TestEnvironment { EnvironmentName = name };
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class EmptyTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class FakeServices : IApplicationSubmissionService, ICandidateApplicationListService, IApplicationWithdrawalService,
        IStudioApplicantListService, IStudioApplicantDetailService, IStudioApplicationStatusService, ICandidateApplicationDetailService
    {
        public int Calls; public Guid Actor; public Guid Application; public SubmitApplicationCommand? Command;
        public Task<Result<ApplicationReviewOffer>> PrepareAsync(Guid actorUserId, Guid jobId, string? screeningAnswer, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Result.Failure<ApplicationReviewOffer>(Error.Validation("test", "test"))); }
        public Task<Result<SubmittedApplication>> SubmitAsync(SubmitApplicationCommand command, CancellationToken cancellationToken = default)
        { Calls++; Command = command; return Task.FromResult(Result.Success(new SubmittedApplication(Guid.NewGuid(), DateTimeOffset.UtcNow))); }
        public Task<Result<ApplicationWithdrawalResult>> WithdrawAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default)
        { Calls++; Actor = actorUserId; Application = applicationId; return Task.FromResult(Result.Success(new ApplicationWithdrawalResult(true))); }
        public Task<Result<CandidateApplicationPage>> ListAsync(Guid actorUserId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new CandidateApplicationPage(Array.Empty<CandidateApplicationListItem>(), page, false)));
        public Task<Result<StudioApplicantPage>> ListAsync(Guid actorUserId, Guid jobId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default, ApplicationStatus? status = null) =>
            Task.FromResult(Result.Success(new StudioApplicantPage(Array.Empty<StudioApplicantListItem>(), false)));
        Task<Result<CandidateApplicationDetail>> ICandidateApplicationDetailService.GetAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure<CandidateApplicationDetail>(Error.NotFound("test", "test")));
        public Task<Result<StudioApplicantDetail>> GetAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Failure<StudioApplicantDetail>(Error.NotFound("test", "test")));
        public Task<Result> ChangeStatusAsync(Guid actorUserId, Guid applicationId, ApplicationStatus newStatus, CancellationToken cancellationToken = default)
        { Calls++; Actor = actorUserId; Application = applicationId; return Task.FromResult(Result.Success()); }
    }
}
