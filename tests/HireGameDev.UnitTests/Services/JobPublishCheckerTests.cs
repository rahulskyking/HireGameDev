using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using HireGameDev.UnitTests.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class JobPublishCheckerTests
{
    [Fact]
    public async Task RealChecker_PublishesVerifiedStudioDirectly_WithoutAdminApproval()
    {
        await using var h = await Harness.Create();
        var draft = await h.Save(JobPublishRulesTests.Valid());
        var check = await h.Check(draft);
        Assert.True(check.CanPublish);
        Assert.False(h.Db.ChangeTracker.HasChanges());
        var result = await h.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Applied);
        Assert.Equal(JobStatus.Active, result.Value.Job.Status);
        Assert.Single(await h.Db.JobPublicationEvents.ToArrayAsync());
        Assert.Contains(await h.Db.JobStatusHistory.ToArrayAsync(), x => x.NewStatus == JobStatus.Active && x.ActorUserId == h.Owner);
    }

    [Fact]
    public async Task SalarySpreadSetting_IsReadFromExistingSettingsService()
    {
        await using var h = await Harness.Create("2");
        var draft = await h.Save(JobPublishRulesTests.Valid() with { SalaryMax = 19 });
        Assert.True((await h.Check(draft)).CanPublish);
    }

    [Fact]
    public async Task InvalidPersistedSpreadSetting_DeniesPublishing()
    {
        await using var h = await Harness.Create("0");
        var draft = await h.Save(JobPublishRulesTests.Valid());
        Assert.Contains((await h.Check(draft)).Findings, x => x.Code == "Jobs.InvalidSalarySpreadSetting");
    }

    [Fact]
    public async Task BlockingContent_DoesNotChangeStatusOrWritePublicationHistory()
    {
        await using var h = await Harness.Create();
        var draft = await h.Save(JobPublishRulesTests.Valid() with { SalaryMin = 3, SalaryMax = 40 });
        var rejected = await h.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
        Assert.False(rejected.Value.Applied);
        Assert.Contains(rejected.Value.Findings, x => x.Code == "Jobs.SalarySpread");
        Assert.Equal(JobStatus.Draft, (await h.Db.Jobs.SingleAsync()).Status);
        Assert.Empty(await h.Db.JobPublicationEvents.ToArrayAsync());
        Assert.Single(await h.Db.JobStatusHistory.ToArrayAsync());
    }

    [Fact]
    public async Task ReferenceDataAndStudioRestrictions_AreRecheckedAtPublication()
    {
        await using var h = await Harness.Create();
        var draft = await h.Save(JobPublishRulesTests.Valid() with { WorkMode = JobWorkMode.Onsite, CityId = 1 });
        var skill = await h.Db.Skills.FindAsync(1); skill!.IsActive = false;
        var city = await h.Db.Cities.FindAsync(1); city!.IsActive = false;
        var studio = await h.Db.StudioProfiles.FindAsync(h.Studio); studio!.SuspendedAt = h.Clock.UtcNow;
        await h.Db.SaveChangesAsync();
        var check = await h.Check(draft);
        Assert.False(check.CanPublish);
        foreach (var code in new[] { "Jobs.InactiveSkills", "Jobs.InactiveCity", "Jobs.StudioRestricted" })
            Assert.Contains(check.Findings, x => x.Code == code);
    }

    [Fact]
    public async Task WrongStudioJobContext_IsRejected()
    {
        await using var h = await Harness.Create();
        var draft = await h.Save(JobPublishRulesTests.Valid());
        var check = await h.Checker.CheckAsync(new(draft.Id, Guid.NewGuid(), draft.Content));
        Assert.False(check.CanPublish);
        Assert.Contains(check.Findings, x => x.Code == "Jobs.NotFound");
    }

    [Fact]
    public async Task SameStudioDuplicate_Blocked_ButOwnJobExcluded()
    {
        await using var h = await Harness.Create();
        var first = await h.Save(JobPublishRulesTests.Valid());
        await h.Service.TransitionAsync(new(h.Owner, h.Studio, first.Id, JobLifecycleAction.Publish));
        Assert.True((await h.Check(first)).CanPublish);
        var copy = await h.Save(JobPublishRulesTests.Valid() with { Description = JobPublishRulesTests.Valid().Description + " Another project." });
        Assert.Contains((await h.Check(copy)).Findings, x => x.Code == "Jobs.Duplicate");
        var denied = await h.Service.TransitionAsync(new(h.Owner, h.Studio, copy.Id, JobLifecycleAction.Publish));
        Assert.False(denied.Value.Applied);
    }

    [Fact]
    public async Task RecentIdenticalContent_RemainsBlockedAfterClosure_AndCooldownClears()
    {
        await using var h = await Harness.Create();
        var first = await h.Save(JobPublishRulesTests.Valid());
        await h.Service.TransitionAsync(new(h.Owner, h.Studio, first.Id, JobLifecycleAction.Publish));
        await h.Service.TransitionAsync(new(h.Owner, h.Studio, first.Id, JobLifecycleAction.Close));
        var copy = await h.Save(JobPublishRulesTests.Valid() with { Openings = 2 });
        Assert.Contains((await h.Check(copy)).Findings, x => x.Code == "Jobs.RepeatCooldown");
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(7);
        Assert.True((await h.Check(copy)).CanPublish);
    }

    [Fact]
    public async Task OrdinaryWarnings_DoNotRequireAdminOrBlockPublication()
    {
        await using var h = await Harness.Create();
        var content = JobPublishRulesTests.Valid() with { FresherFriendly = true, MinYears = 2 };
        var draft = await h.Save(content);
        var result = await h.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
        Assert.True(result.Value.Applied);
        Assert.Contains(result.Value.Findings, x => x.Code == "Jobs.FresherExperienceMismatch");
        Assert.Equal(content.Description, result.Value.Job.Content.Description);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public TestClock Clock { get; } = new();
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Studio { get; } = Guid.NewGuid();
        public JobPublishChecker Checker { get; }
        public JobLifecycleService Service { get; }
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private Harness()
        {
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Clock);
            var limits = Options.Create(new JobLifecycleOptions());
            var audit = new NullAudit();
            Checker = new(Db, new SettingsService(Db, _cache, audit, Clock), new JobDuplicateDetector(Db, Clock, limits));
            Service = new(Db, Clock, Checker, audit, limits);
        }
        public static async Task<Harness> Create(string? spread = null)
        {
            var h = new Harness();
            h.Db.Users.Add(new ApplicationUser { Id = h.Owner, AccountType = AccountType.Studio, UserName = "studio@example.com", Email = "studio@example.com" });
            h.Db.StudioProfiles.Add(new StudioProfile { Id = h.Studio, OwnerUserId = h.Owner, Name = "Test", Slug = "test", VerificationStatus = StudioVerificationStatus.Verified });
            h.Db.StudioMembers.Add(new StudioMember { StudioId = h.Studio, UserId = h.Owner, MemberRole = StudioMemberRole.Owner });
            h.Db.Cities.Add(new City { Id = 1, Name = "Pune", Slug = "pune", StateCode = "MH", IsActive = true });
            for (var i = 1; i <= 3; i++) h.Db.Skills.Add(new Skill { Id = i, Name = $"Skill {i}", Slug = $"skill-{i}" });
            if (spread is not null) h.Db.AppSettings.Add(new AppSetting { Key = "job.salary_max_spread", Value = spread });
            await h.Db.SaveChangesAsync();
            return h;
        }
        public async Task<JobSnapshot> Save(JobContent content)
        {
            var result = await Service.SaveAsync(new(Owner, Studio, content));
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value;
        }
        public Task<JobPublishCheckResult> Check(JobSnapshot job) => Checker.CheckAsync(new(job.Id, Studio, job.Content));
        public async ValueTask DisposeAsync() { _cache.Dispose(); await Db.DisposeAsync(); }
    }
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class NullAudit : IAuditLogger
    {
        public void Enqueue(AuditLogEntry entry) { }
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
