using System.Reflection;
using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using HireGameDev.Web.Controllers;
using HireGameDev.Web.Models.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace HireGameDev.UnitTests.Web;

public sealed class StudioJobsControllerTests
{
    [Fact]
    public async Task Preview_IsPrivateReadOnly_UsesSavedContentAndShowsWarningsWithoutBlocking()
    {
        var h = new Harness();
        h.Jobs.Snapshot = h.Jobs.Snapshot with { Content = new JobContent { Title = "Saved title" } };
        h.Checker.Findings = new[] { new JobPublishFinding("Jobs.DescriptionDetail", JobFindingSeverity.Warning, "Description", "Add detail.") };
        var result = await h.Controller.Preview(h.Jobs.Snapshot.Id, h.Studio, default);
        var page = Assert.IsType<JobPreviewPage>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("private, no-store", h.Controller.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("noindex, nofollow", h.Controller.Response.Headers["X-Robots-Tag"].ToString());
        Assert.True(page.CanWrite);
        Assert.True(page.Assessment!.CanPublish);
        Assert.Equal(JobLifecycleAction.Publish, page.ConfirmationAction);
        Assert.Equal("Saved title", h.Checker.LastContext!.Content.Title);
        Assert.Equal(h.Studio, h.Checker.LastContext.StudioId);
        Assert.Null(h.Jobs.LastSave);
        Assert.Null(h.Jobs.LastTransition);
    }

    [Fact]
    public async Task Preview_RejectsOtherStudioAndWrongJobBeforeCheckingContent()
    {
        var h = new Harness();
        Assert.IsType<ForbidResult>(await h.Controller.Preview(h.Jobs.Snapshot.Id, Guid.NewGuid(), default));
        Assert.Null(h.Checker.LastContext);
        Assert.IsType<NotFoundResult>(await h.Controller.Preview(Guid.NewGuid(), h.Studio, default));
        Assert.Null(h.Checker.LastContext);
    }

    [Fact]
    public async Task ViewerPreview_ContainsNoAssessmentOrMutationControls()
    {
        var h = new Harness(StudioMemberRole.Viewer);
        var result = await h.Controller.Preview(h.Jobs.Snapshot.Id, h.Studio, default);
        var page = Assert.IsType<JobPreviewPage>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(page.CanWrite);
        Assert.Null(page.Assessment);
        Assert.Null(page.ConfirmationAction);
        Assert.Null(h.Checker.LastContext);
    }

    [Theory]
    [InlineData(JobStatus.Paused, JobLifecycleAction.Resume)]
    [InlineData(JobStatus.Full, JobLifecycleAction.ReopenFull)]
    [InlineData(JobStatus.Expired, JobLifecycleAction.Renew)]
    [InlineData(JobStatus.Closed, null)]
    [InlineData(JobStatus.Removed, null)]
    [InlineData(JobStatus.Active, null)]
    public async Task Preview_OffersOnlyExplicitStatusAppropriateAction(JobStatus status, JobLifecycleAction? action)
    {
        var h = new Harness();
        h.Jobs.Snapshot = h.Jobs.Snapshot with { Status = status };
        var page = Assert.IsType<JobPreviewPage>(Assert.IsType<ViewResult>(await h.Controller.Preview(h.Jobs.Snapshot.Id, h.Studio, default)).Model);
        Assert.Equal(action, page.ConfirmationAction);
    }

    [Fact]
    public async Task Preview_OverduePausedJob_RequiresRenewalWithoutMutatingStatus()
    {
        var h = new Harness();
        h.Jobs.Snapshot = h.Jobs.Snapshot with { Status = JobStatus.Paused, ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var page = Assert.IsType<JobPreviewPage>(Assert.IsType<ViewResult>(await h.Controller.Preview(h.Jobs.Snapshot.Id, h.Studio, default)).Model);
        Assert.Equal(JobLifecycleAction.Renew, page.ConfirmationAction);
        Assert.Equal(JobStatus.Paused, h.Jobs.Snapshot.Status);
        Assert.Null(h.Jobs.LastTransition);
    }

    [Fact]
    public async Task PreviewPassed_DoesNotBypassSubsequentPublishRejection()
    {
        var h = new Harness();
        var preview = Assert.IsType<JobPreviewPage>(Assert.IsType<ViewResult>(await h.Controller.Preview(h.Jobs.Snapshot.Id, h.Studio, default)).Model);
        Assert.True(preview.Assessment!.CanPublish);
        h.Jobs.Outcome = Result.Success(new JobLifecycleOutcome(false, h.Jobs.Snapshot,
            new[] { new JobPublishFinding("Jobs.StudioRestricted", JobFindingSeverity.PostingRestriction, null, "Verification revoked.") }));
        var result = await h.Controller.Transition(h.Jobs.Snapshot.Id, h.Studio, JobLifecycleAction.Publish, h.Jobs.Snapshot.Version, true, default);
        var blocked = Assert.IsType<JobManagePage>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Jobs.StudioRestricted", Assert.Single(blocked.Findings!).Code);
        Assert.NotNull(h.Jobs.LastTransition);
        Assert.False(h.Controller.TempData.ContainsKey("StatusMessage"));
    }

    [Fact]
    public async Task SaveAndPreview_SavesCurrentFormBeforePreviewing()
    {
        var h = new Harness();
        var result = await h.Controller.Save(new JobEditorModel { StudioId = h.Studio, Content = new JobContent { Title = "Current edit" } }, "preview", default);
        Assert.Equal("Preview", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("Current edit", h.Jobs.LastSave!.Content.Title);
    }

    [Fact]
    public async Task Autosave_RequiresExistingJobAndVersion_AndViewerCannotWrite()
    {
        var h = new Harness();
        var invalid = await h.Controller.Autosave(new JobEditorModel { StudioId = h.Studio }, default);
        Assert.IsType<BadRequestObjectResult>(invalid);
        Assert.Null(h.Jobs.LastSave);
        var viewer = new Harness(StudioMemberRole.Viewer);
        var denied = Assert.IsType<ObjectResult>(await viewer.Controller.Autosave(new JobEditorModel { StudioId = viewer.Studio }, default));
        Assert.Equal(403, denied.StatusCode);
        Assert.Null(viewer.Jobs.LastSave);
        Assert.IsType<ForbidResult>(await viewer.Controller.Duplicate(viewer.Jobs.Snapshot.Id, viewer.Studio, default));
    }

    [Fact]
    public async Task Autosave_UsesTrustedActorAndExpectedVersion_ReturnsJson()
    {
        var h = new Harness();
        var model = new JobEditorModel { StudioId = h.Studio, JobId = h.Jobs.Snapshot.Id, Version = 7,
            Content = new JobContent { Title = "Edited role" } };
        Assert.IsType<JsonResult>(await h.Controller.Autosave(model, default));
        Assert.Equal(h.Actor, h.Jobs.LastSave!.ActorUserId);
        Assert.Equal((uint?)7, h.Jobs.LastSave.ExpectedVersion);
        Assert.Equal(model.JobId, h.Jobs.LastSave.JobId);
    }

    [Fact]
    public async Task Autosave_StaleOrNoLongerEditable_ReturnsConflict()
    {
        var h = new Harness();
        var model = new JobEditorModel { StudioId = h.Studio, JobId = h.Jobs.Snapshot.Id, Version = 7 };
        h.Jobs.SaveError = Error.Conflict("Jobs.StaleVersion", "Reload.");
        Assert.Equal(409, Assert.IsType<ObjectResult>(await h.Controller.Autosave(model, default)).StatusCode);
        h.Jobs.SaveError = Error.Validation("Jobs.NotEditable", "Only drafts and paused jobs can be edited.");
        Assert.Equal(409, Assert.IsType<ObjectResult>(await h.Controller.Autosave(model, default)).StatusCode);
    }

    [Fact]
    public async Task PublishBlocked_ShowsStructuredReasonsAndDraftStatus_NotSuccess()
    {
        var h = new Harness();
        var finding = new JobPublishFinding("Jobs.SalarySpread", JobFindingSeverity.BlockingError, "SalaryMax", "Narrow the salary range.");
        h.Jobs.Outcome = Result.Success(new JobLifecycleOutcome(false, h.Jobs.Snapshot, new[] { finding }));
        var result = await h.Controller.Transition(h.Jobs.Snapshot.Id, h.Studio, JobLifecycleAction.Publish, 7, true, default);
        var view = Assert.IsType<ViewResult>(result);
        var page = Assert.IsType<JobManagePage>(view.Model);
        Assert.Equal("Manage", view.ViewName);
        Assert.Equal(JobStatus.Draft, page.Job.Status);
        Assert.Equal(finding, Assert.Single(page.Findings!));
        Assert.Contains("not completed", page.Error!);
        Assert.False(h.Controller.TempData.ContainsKey("StatusMessage"));
        Assert.Equal(h.Actor, h.Jobs.LastTransition!.ActorUserId);
        Assert.Equal((uint?)7, h.Jobs.LastTransition.ExpectedVersion);
    }

    [Fact]
    public async Task PublishApplied_ReportsActiveAndRedirectsForFreshRead()
    {
        var h = new Harness();
        h.Jobs.Outcome = Result.Success(new JobLifecycleOutcome(true, h.Jobs.Snapshot with { Status = JobStatus.Active }, Array.Empty<JobPublishFinding>()));
        var result = await h.Controller.Transition(h.Jobs.Snapshot.Id, h.Studio, JobLifecycleAction.Publish, 7, true, default);
        Assert.Equal("Manage", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("now Active", Assert.IsType<string>(h.Controller.TempData["StatusMessage"]));
    }

    [Fact]
    public async Task StaleVersion_ShowsErrorAndReloadsCurrentVersion()
    {
        var h = new Harness();
        h.Jobs.Snapshot = h.Jobs.Snapshot with { Version = 99 };
        h.Jobs.Outcome = Result.Failure<JobLifecycleOutcome>(Error.Conflict("Jobs.StaleVersion", "The job changed. Reload it."));
        var result = await h.Controller.Transition(h.Jobs.Snapshot.Id, h.Studio, JobLifecycleAction.Publish, 7, true, default);
        var page = Assert.IsType<JobManagePage>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal((uint)99, page.Job.Version);
        Assert.Equal("The job changed. Reload it.", page.Error);
        Assert.False(h.Controller.TempData.ContainsKey("StatusMessage"));
    }

    [Fact]
    public async Task MissingConfirmation_DoesNotCallLifecycle()
    {
        var h = new Harness();
        var result = await h.Controller.Transition(h.Jobs.Snapshot.Id, h.Studio, JobLifecycleAction.Publish, 7, false, default);
        Assert.IsType<ViewResult>(result);
        Assert.Null(h.Jobs.LastTransition);
    }

    [Fact]
    public async Task InvalidModelBinding_ShowsReloadMessageWithoutCallingLifecycle()
    {
        var h = new Harness();
        h.Controller.ModelState.AddModelError("version", "Missing version");
        var result = await h.Controller.Transition(h.Jobs.Snapshot.Id, h.Studio, JobLifecycleAction.Publish, 0, true, default);
        Assert.IsType<ViewResult>(result);
        Assert.Null(h.Jobs.LastTransition);
    }

    [Fact]
    public async Task ViewerCannotPublishEvenByDirectPost()
    {
        var h = new Harness(StudioMemberRole.Viewer);
        var result = await h.Controller.Transition(h.Jobs.Snapshot.Id, h.Studio, JobLifecycleAction.Publish, 7, true, default);
        Assert.IsType<ForbidResult>(result);
        Assert.Null(h.Jobs.LastTransition);
    }

    [Fact]
    public async Task SaveAndManage_PersistsBeforeOpeningManagement()
    {
        var h = new Harness();
        var model = new JobEditorModel { StudioId = h.Studio, Content = new JobContent { Title = "Saved role" } };
        var result = await h.Controller.Save(model, "manage", default);
        Assert.Equal("Manage", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("Saved role", h.Jobs.LastSave!.Content.Title);
        Assert.Equal(h.Actor, h.Jobs.LastSave.ActorUserId);
    }

    private sealed class Harness
    {
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid Studio { get; } = Guid.NewGuid();
        public FakeJobs Jobs { get; }
        public StudioJobsController Controller { get; }
        public PreviewChecker Checker { get; } = new();
        public Harness(StudioMemberRole role = StudioMemberRole.Owner)
        {
            Jobs = new FakeJobs(Studio);
            var studios = DispatchProxy.Create<IStudioProfileService, MembershipProxy>();
            ((MembershipProxy)(object)studios).Memberships = new[] { new StudioMembershipSummary(Studio, "Studio", "studio", null, role, StudioVerificationStatus.Verified, null) };
            var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Actor.ToString()) }, "test")) };
            var clock = new SystemClock();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, clock);
            Controller = new(Jobs, studios, DispatchProxy.Create<IReferenceDataService, LookupProxy>(), Checker, db, clock)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
                TempData = new TempDataDictionary(context, new EmptyTempDataProvider())
            };
        }
    }
    // DispatchProxy's generated subclass requires an accessible, non-sealed base.
    public class MembershipProxy : DispatchProxy
    {
        public IReadOnlyList<StudioMembershipSummary> Memberships { get; set; } = Array.Empty<StudioMembershipSummary>();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IStudioProfileService.GetUserMembershipsAsync)
                ? Task.FromResult(Memberships) : throw new NotSupportedException();
    }
    public class LookupProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IReferenceDataService.GetActiveSkillsAsync) => Task.FromResult<IReadOnlyList<SkillLookupItem>>(Array.Empty<SkillLookupItem>()),
            nameof(IReferenceDataService.GetActiveCitiesAsync) => Task.FromResult<IReadOnlyList<CityLookupItem>>(Array.Empty<CityLookupItem>()),
            _ => throw new NotSupportedException()
        };
    }
    private sealed class PreviewChecker : IJobPublishChecker
    {
        public JobPublishContext? LastContext { get; private set; }
        public IReadOnlyList<JobPublishFinding> Findings { get; set; } = Array.Empty<JobPublishFinding>();
        public Task<JobPublishCheckResult> CheckAsync(JobPublishContext context, CancellationToken cancellationToken = default)
        { LastContext = context; return Task.FromResult(new JobPublishCheckResult(Findings)); }
    }
    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class FakeJobs : IJobLifecycleService
    {
        public JobSnapshot Snapshot { get; set; }
        public Result<JobLifecycleOutcome> Outcome { get; set; }
        public JobTransitionCommand? LastTransition { get; private set; }
        public SaveJobCommand? LastSave { get; private set; }
        public Error? SaveError { get; set; }
        public FakeJobs(Guid studio)
        {
            Snapshot = new(Guid.NewGuid(), studio, "job-test", JobStatus.Draft, new JobContent(), 50, 0, null, null, null, 7);
            Outcome = Result.Success(new JobLifecycleOutcome(false, Snapshot, Array.Empty<JobPublishFinding>()));
        }
        public Task<Result<JobSnapshot>> GetAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(studioId == Snapshot.StudioId && jobId == Snapshot.Id ? Result.Success(Snapshot) : Result.Failure<JobSnapshot>(Error.NotFound("Jobs.NotFound", "Not found.")));
        public Task<Result<JobSnapshot>> SaveAsync(SaveJobCommand command, CancellationToken cancellationToken = default)
        { LastSave = command; return Task.FromResult(SaveError is null ? Result.Success(Snapshot with { Content = command.Content }) : Result.Failure<JobSnapshot>(SaveError)); }
        public Task<Result<JobLifecycleOutcome>> TransitionAsync(JobTransitionCommand command, CancellationToken cancellationToken = default)
        { LastTransition = command; return Task.FromResult(Outcome); }
        public Task<Result<StudioJobList>> ListAsync(Guid actorUserId, Guid studioId, JobStatus? status = null, int page = 1, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<JobSnapshot>> DuplicateAsync(Guid actorUserId, Guid studioId, Guid sourceJobId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success(Snapshot));
        public Task<Result> DeleteDraftAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RemoveAsync(RemoveJobCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RestrictStudioAsync(StudioJobRestrictionCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ExpireDueJobsAsync(int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
