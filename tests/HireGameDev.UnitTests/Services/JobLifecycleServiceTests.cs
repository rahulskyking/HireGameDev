using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class JobLifecycleServiceTests
{
    [Fact]
    public async Task Drafts_CanBeIncomplete_AndDeleted_WithHistoryAndSkills()
    {
        await using var h = await Harness.Create();
        var result = await h.Service.SaveAsync(new(h.Owner, h.Studio, new JobContent()));
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Content.Title);
        Assert.Single(await h.Db.JobStatusHistory.ToArrayAsync());
        Assert.True((await h.Service.DeleteDraftAsync(h.Owner, h.Studio, result.Value.Id)).IsSuccess);
        Assert.Empty(await h.Db.Jobs.ToArrayAsync());
        Assert.Empty(await h.Db.JobStatusHistory.ToArrayAsync());
    }

    [Fact]
    public async Task DraftLimitsAndStaleEdits_ReturnStructuredErrors()
    {
        await using var h = await Harness.Create(new JobLifecycleOptions { MaxDraftsPerStudio = 1 });
        var job = await h.Draft();
        var limit = await h.Service.SaveAsync(new(h.Owner, h.Studio, new JobContent()));
        Assert.Equal("Jobs.DraftLimit", limit.Error.Code);
        var stale = await h.Service.SaveAsync(new(h.Owner, h.Studio, Harness.Content(), job.Id, 123));
        Assert.Equal("Jobs.StaleVersion", stale.Error.Code);
        var missingVersion = await h.Service.SaveAsync(new(h.Owner, h.Studio, Harness.Content(), job.Id));
        Assert.Equal("Jobs.StaleVersion", missingVersion.Error.Code);
        var saved = await h.Service.SaveAsync(new(h.Owner, h.Studio, Harness.Content("Updated developer"), job.Id, job.Version));
        Assert.True(saved.IsSuccess);
        Assert.Equal("Updated developer", saved.Value.Content.Title);
    }

    [Fact]
    public async Task BackendAuthorization_DeniesViewerWritesCandidateAndOtherStudioJobIds()
    {
        await using var h = await Harness.Create();
        var job = await h.Draft();
        Assert.True((await h.Service.GetAsync(h.Viewer, h.Studio, job.Id)).IsSuccess);
        Assert.Equal("Jobs.Forbidden", (await h.Service.SaveAsync(new(h.Viewer, h.Studio, new JobContent()))).Error.Code);
        Assert.Equal("Jobs.Forbidden", (await h.Service.TransitionAsync(new(h.Viewer, h.Studio, job.Id, JobLifecycleAction.Publish))).Error.Code);
        Assert.Equal("Jobs.Forbidden", (await h.Service.SaveAsync(new(h.Candidate, h.Studio, new JobContent()))).Error.Code);
        var otherStudio = Guid.NewGuid();
        h.Db.StudioProfiles.Add(new StudioProfile { Id = otherStudio, OwnerUserId = h.Owner, Name = "Other", Slug = "other" });
        h.Db.StudioMembers.Add(new StudioMember { StudioId = otherStudio, UserId = h.Owner, MemberRole = StudioMemberRole.Owner });
        await h.Db.SaveChangesAsync();
        Assert.Equal("Jobs.NotFound", (await h.Service.TransitionAsync(new(h.Owner, otherStudio, job.Id, JobLifecycleAction.Publish))).Error.Code);
        Assert.Equal("Jobs.NotFound", (await h.Service.GetAsync(h.Owner, otherStudio, job.Id)).Error.Code);
        Assert.True((await h.Service.TransitionAsync(new(h.Recruiter, h.Studio, job.Id, JobLifecycleAction.Publish))).Value.Applied);
    }

    [Fact]
    public async Task BlockingFindingsPreventActivation_AndWarningsDoNotBlock()
    {
        await using var h = await Harness.Create();
        var job = await h.Draft();
        var disabled = h.WithChecker(new FakeChecker(new JobPublishFinding("Jobs.SalarySpread", JobFindingSeverity.BlockingError, "SalaryMax", "Narrow the salary range.")));
        var denied = await disabled.TransitionAsync(new(h.Owner, h.Studio, job.Id, JobLifecycleAction.Publish));
        Assert.False(denied.Value.Applied);
        Assert.Equal("Jobs.SalarySpread", denied.Value.Findings.Single().Code);
        Assert.Empty(await h.Db.JobPublicationEvents.ToArrayAsync());
        var warned = h.WithChecker(new FakeChecker(new JobPublishFinding("quality", JobFindingSeverity.Warning, "Description", "Add more detail.")));
        var published = await warned.TransitionAsync(new(h.Owner, h.Studio, job.Id, JobLifecycleAction.Publish));
        Assert.True(published.Value.Applied);
        Assert.Single(published.Value.Findings);
    }

    [Fact]
    public async Task PublishPauseResume_PreserveDatesAndDoNotConsumeAnotherPostingEvent()
    {
        await using var h = await Harness.Create();
        var job = await h.Draft();
        var active = await h.Act(job.Id, JobLifecycleAction.Publish);
        Assert.True(active.Applied);
        Assert.Equal(JobStatus.Active, active.Job.Status);
        var expiry = active.Job.ExpiresAt;
        Assert.Equal(h.Clock.UtcNow.AddDays(30), expiry);
        Assert.True((await h.Act(job.Id, JobLifecycleAction.Publish)).Applied);
        Assert.Single(await h.Db.JobPublicationEvents.ToArrayAsync());
        Assert.Equal("Jobs.NotEditable", (await h.Service.SaveAsync(new(h.Owner, h.Studio, Harness.Content(), job.Id, active.Job.Version))).Error.Code);
        Assert.Equal("Jobs.CannotDelete", (await h.Service.DeleteDraftAsync(h.Owner, h.Studio, job.Id)).Error.Code);
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(2);
        Assert.True((await h.Act(job.Id, JobLifecycleAction.Pause)).Applied);
        var resumed = await h.Act(job.Id, JobLifecycleAction.Resume);
        Assert.True(resumed.Applied);
        Assert.Equal(expiry, resumed.Job.ExpiresAt);
        Assert.Equal(active.Job.PublishedAt, resumed.Job.PublishedAt);
        Assert.Single(await h.Db.JobPublicationEvents.ToArrayAsync());
        Assert.Equal(4, await h.Db.JobStatusHistory.CountAsync());
    }

    [Fact]
    public async Task FullJobs_RequireExplicitReopeningAndAvailableCapacity()
    {
        await using var h = await Harness.Create();
        var job = await h.Draft();
        await h.Act(job.Id, JobLifecycleAction.Publish);
        var stored = await h.Db.Jobs.SingleAsync();
        stored.Status = JobStatus.Full;
        stored.ApplicationsCount = stored.ApplicationCap;
        await h.Db.SaveChangesAsync();
        Assert.False((await h.Act(job.Id, JobLifecycleAction.ReopenFull)).Applied);
        stored = await h.Db.Jobs.SingleAsync();
        stored.ApplicationsCount--;
        await h.Db.SaveChangesAsync();
        Assert.Equal(JobStatus.Full, (await h.Service.GetAsync(h.Owner, h.Studio, job.Id)).Value.Status);
        Assert.True((await h.Act(job.Id, JobLifecycleAction.ReopenFull)).Applied);
        Assert.Single(await h.Db.JobPublicationEvents.ToArrayAsync());
    }

    [Theory]
    [InlineData(JobStatus.Active)]
    [InlineData(JobStatus.Paused)]
    [InlineData(JobStatus.Full)]
    public async Task Expiry_IsIdempotent_AndRenewalSetsFreshDates(JobStatus initial)
    {
        await using var h = await Harness.Create();
        var job = await h.Draft();
        var publication = await h.Act(job.Id, JobLifecycleAction.Publish);
        var stored = await h.Db.Jobs.SingleAsync();
        stored.Status = initial;
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = publication.Job.ExpiresAt!.Value;
        Assert.Equal(1, await h.Service.ExpireDueJobsAsync());
        Assert.Equal(0, await h.Service.ExpireDueJobsAsync());
        Assert.False((await h.Act(job.Id, JobLifecycleAction.Resume)).Applied);
        var renewed = await h.Act(job.Id, JobLifecycleAction.Renew);
        Assert.True(renewed.Applied);
        Assert.Equal(publication.Job.FirstPublishedAt, renewed.Job.FirstPublishedAt);
        Assert.Equal(h.Clock.UtcNow.AddDays(30), renewed.Job.ExpiresAt);
        Assert.Equal(2, await h.Db.JobPublicationEvents.CountAsync());
    }

    [Fact]
    public async Task ClosedAndRemoved_AreTerminalForStudio_AdminRemovalRequiresReason()
    {
        await using var h = await Harness.Create();
        var job = await h.Draft();
        await h.Act(job.Id, JobLifecycleAction.Publish);
        Assert.True((await h.Act(job.Id, JobLifecycleAction.Close)).Applied);
        Assert.False((await h.Act(job.Id, JobLifecycleAction.Publish)).Applied);
        Assert.False((await h.Act(job.Id, JobLifecycleAction.Renew)).Applied);
        Assert.Equal("Jobs.Forbidden", (await h.Service.RemoveAsync(new(h.Owner, job.Id, "Policy violation"))).Error.Code);
        Assert.Equal("Jobs.ReasonRequired", (await h.Service.RemoveAsync(new(h.Admin, job.Id, " "))).Error.Code);
        Assert.True((await h.Service.RemoveAsync(new(h.Admin, job.Id, "Policy violation"))).IsSuccess);
        var removed = await h.Db.Jobs.SingleAsync();
        Assert.Equal(JobStatus.Removed, removed.Status);
        Assert.Equal(h.Admin, removed.RemovedByUserId);
        Assert.Equal("Policy violation", removed.RemovalReason);
        Assert.NotNull(removed.ClosedAt);
        Assert.False((await h.Act(job.Id, JobLifecycleAction.Resume)).Applied);
        Assert.Single(h.Audit.Entries);
        Assert.Equal("admin_removed", (await h.Db.JobStatusHistory.OrderBy(x => x.OccurredAt).ToArrayAsync()).Last(x => x.NewStatus == JobStatus.Removed).ReasonCode);
    }

    [Fact]
    public async Task SuspensionAndVerification_BlockActivation_WithoutAutoRestoration()
    {
        await using var h = await Harness.Create();
        var job = await h.Draft();
        var studio = await h.Db.StudioProfiles.SingleAsync();
        studio.VerificationStatus = StudioVerificationStatus.Unverified;
        await h.Db.SaveChangesAsync();
        Assert.False((await h.Act(job.Id, JobLifecycleAction.Publish)).Applied);
        studio = await h.Db.StudioProfiles.SingleAsync();
        studio.VerificationStatus = StudioVerificationStatus.Verified;
        await h.Db.SaveChangesAsync();
        await h.Act(job.Id, JobLifecycleAction.Publish);
        Assert.True((await h.Service.RestrictStudioAsync(new(h.Admin, h.Studio, StudioJobRestrictionAction.Suspend, "Investigating reports"))).IsSuccess);
        Assert.Equal(JobStatus.Paused, (await h.Db.Jobs.SingleAsync()).Status);
        Assert.False((await h.Act(job.Id, JobLifecycleAction.Resume)).Applied);
        Assert.True((await h.Service.RestrictStudioAsync(new(h.Admin, h.Studio, StudioJobRestrictionAction.LiftSuspension, "Investigation resolved"))).IsSuccess);
        Assert.Equal(JobStatus.Paused, (await h.Db.Jobs.SingleAsync()).Status);
        Assert.True((await h.Act(job.Id, JobLifecycleAction.Resume)).Applied);
        var currentJob = await h.Db.Jobs.SingleAsync();
        studio = await h.Db.StudioProfiles.SingleAsync();
        Assert.True(JobLifecycleRules.CanAppearPublicly(currentJob, studio, h.Clock.UtcNow));
        studio.VerificationStatus = StudioVerificationStatus.Rejected;
        Assert.False(JobLifecycleRules.CanAppearPublicly(currentJob, studio, h.Clock.UtcNow));
    }

    [Fact]
    public async Task FullConsumesSlot_ClosingDoesNotEraseQuotaOrRepeatHistory()
    {
        await using var h = await Harness.Create(new JobLifecycleOptions { FreeActiveJobs = 1 });
        var first = await h.Draft();
        await h.Act(first.Id, JobLifecycleAction.Publish);
        var stored = await h.Db.Jobs.SingleAsync(); stored.Status = JobStatus.Full; await h.Db.SaveChangesAsync();
        var second = await h.Draft("Different developer");
        var rejected = await h.Act(second.Id, JobLifecycleAction.Publish);
        Assert.False(rejected.Applied);
        Assert.Equal("Jobs.ActiveLimit", rejected.Findings.Single().Code);
        await h.Act(first.Id, JobLifecycleAction.Close);
        var copied = await h.Service.SaveAsync(new(h.Owner, h.Studio, Harness.Content() with { Openings = 2, SalaryMax = 13 }));
        Assert.True(copied.IsSuccess);
        var copy = copied.Value;
        var repeated = await h.Act(copy.Id, JobLifecycleAction.Publish);
        Assert.Equal("Jobs.RepeatCooldown", repeated.Findings.Single().Code);
        Assert.True((await h.Act(second.Id, JobLifecycleAction.Publish)).Applied);
        Assert.Equal(2, await h.Db.JobPublicationEvents.CountAsync());
    }

    [Fact]
    public async Task RollingPostingQuota_PersistsAfterClosure_ThenClears()
    {
        await using var h = await Harness.Create(new JobLifecycleOptions { PublicationsPer24Hours = 1 });
        var first = await h.Draft(); await h.Act(first.Id, JobLifecycleAction.Publish); await h.Act(first.Id, JobLifecycleAction.Close);
        var second = await h.Draft("Another developer");
        var rejected = await h.Act(second.Id, JobLifecycleAction.Publish);
        Assert.Equal("Jobs.PostingLimit", rejected.Findings.Single().Code);
        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(24);
        Assert.True((await h.Act(second.Id, JobLifecycleAction.Publish)).Applied);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public TestClock Clock { get; } = new();
        public TestAudit Audit { get; } = new();
        public Guid Studio { get; } = Guid.NewGuid();
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Recruiter { get; } = Guid.NewGuid();
        public Guid Viewer { get; } = Guid.NewGuid();
        public Guid Candidate { get; } = Guid.NewGuid();
        public Guid Admin { get; } = Guid.NewGuid();
        private readonly JobLifecycleOptions _options;
        public JobLifecycleService Service { get; }
        private Harness(JobLifecycleOptions options)
        {
            _options = options;
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Clock);
            Service = WithChecker(new FakeChecker());
        }
        public JobLifecycleService WithChecker(IJobPublishChecker checker) => new(Db, Clock, checker, Audit, Options.Create(_options));
        public static async Task<Harness> Create(JobLifecycleOptions? options = null)
        {
            var h = new Harness(options ?? new JobLifecycleOptions());
            foreach (var (id, type) in new[] { (h.Owner, AccountType.Studio), (h.Recruiter, AccountType.Studio), (h.Viewer, AccountType.Studio), (h.Candidate, AccountType.Candidate), (h.Admin, AccountType.Admin) })
                h.Db.Users.Add(new ApplicationUser { Id = id, UserName = id.ToString(), Email = $"{id:N}@example.com", AccountType = type });
            h.Db.StudioProfiles.Add(new StudioProfile { Id = h.Studio, OwnerUserId = h.Owner, Name = "Test studio", Slug = "test-studio", VerificationStatus = StudioVerificationStatus.Verified });
            foreach (var (id, role) in new[] { (h.Owner, StudioMemberRole.Owner), (h.Recruiter, StudioMemberRole.Recruiter), (h.Viewer, StudioMemberRole.Viewer), (h.Candidate, StudioMemberRole.Owner) })
                h.Db.StudioMembers.Add(new StudioMember { StudioId = h.Studio, UserId = id, MemberRole = role });
            for (var i = 1; i <= 3; i++) h.Db.Skills.Add(new Skill { Id = i, Name = $"Skill {i}", Slug = $"skill-{i}" });
            await h.Db.SaveChangesAsync();
            return h;
        }
        public static JobContent Content(string title = "Unity developer") => new()
        {
            Title = title, Description = new string('a', 120), Category = DisciplineCategory.Programming, JobType = JobType.FullTime,
            SeniorityMin = SeniorityLevel.Junior, SeniorityMax = SeniorityLevel.Mid, MinYears = 1, WorkMode = JobWorkMode.Remote,
            SalaryMin = 10, SalaryMax = 12, PayUnit = JobPayUnit.Lpa,
            Skills = new[] { new JobSkillInput(1, true, SkillProficiency.Working), new JobSkillInput(2, false, SkillProficiency.Working), new JobSkillInput(3, false, SkillProficiency.Working) }
        };
        public async Task<JobSnapshot> Draft(string title = "Unity developer")
        {
            var saved = await Service.SaveAsync(new(Owner, Studio, Content(title)));
            Assert.True(saved.IsSuccess, saved.Error.Message);
            return saved.Value;
        }
        public async Task<JobLifecycleOutcome> Act(Guid jobId, JobLifecycleAction action)
        {
            var result = await Service.TransitionAsync(new(Owner, Studio, jobId, action));
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class FakeChecker(params JobPublishFinding[] findings) : IJobPublishChecker
    {
        public Task<JobPublishCheckResult> CheckAsync(JobPublishContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobPublishCheckResult(findings));
    }
    private sealed class TestAudit : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = new();
        public void Enqueue(AuditLogEntry entry) => Entries.Add(entry);
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) { Enqueue(entry); return Task.CompletedTask; }
    }
}
