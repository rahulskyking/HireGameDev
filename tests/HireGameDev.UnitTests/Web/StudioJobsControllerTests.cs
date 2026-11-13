using System.Reflection;
using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;
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
        public Harness(StudioMemberRole role = StudioMemberRole.Owner)
        {
            Jobs = new FakeJobs(Studio);
            var studios = DispatchProxy.Create<IStudioProfileService, MembershipProxy>();
            ((MembershipProxy)(object)studios).Memberships = new[] { new StudioMembershipSummary(Studio, "Studio", "studio", null, role, StudioVerificationStatus.Verified, null) };
            var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Actor.ToString()) }, "test")) };
            Controller = new(Jobs, studios, null!)
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
        public FakeJobs(Guid studio)
        {
            Snapshot = new(Guid.NewGuid(), studio, "job-test", JobStatus.Draft, new JobContent(), 50, 0, null, null, null, 7);
            Outcome = Result.Success(new JobLifecycleOutcome(false, Snapshot, Array.Empty<JobPublishFinding>()));
        }
        public Task<Result<JobSnapshot>> GetAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success(Snapshot));
        public Task<Result<JobSnapshot>> SaveAsync(SaveJobCommand command, CancellationToken cancellationToken = default)
        { LastSave = command; return Task.FromResult(Result.Success(Snapshot with { Content = command.Content })); }
        public Task<Result<JobLifecycleOutcome>> TransitionAsync(JobTransitionCommand command, CancellationToken cancellationToken = default)
        { LastTransition = command; return Task.FromResult(Outcome); }
        public Task<Result<StudioJobList>> ListAsync(Guid actorUserId, Guid studioId, JobStatus? status = null, int page = 1, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeleteDraftAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RemoveAsync(RemoveJobCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> RestrictStudioAsync(StudioJobRestrictionCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ExpireDueJobsAsync(int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
