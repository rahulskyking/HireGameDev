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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace HireGameDev.UnitTests.Integration;

// Opt-in: never connect to a developer's application database or silently substitute InMemory.
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HGD_TEST_POSTGRES")))
            Skip = "Set HGD_TEST_POSTGRES to a PostgreSQL 16 maintenance connection with CREATEDB privileges.";
    }
}

[Trait("Category", "PostgresIntegration")]
public sealed class JobPostgresTests
{
    [PostgresFact]
    public async Task PublicQueries_TranslateFiltersAndHideRevokedStudioWithoutScheduler()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        await using var session = h.Session();
        var published = await session.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
        Assert.True(published.IsSuccess); Assert.True(published.Value.Applied);
        // No logo in this fixture; resolver must not be called for absent assets.
        var service = new PublicJobQueryService(session.Db, new TestClock(), null!);
        var search = new PublicJobSearch { Keyword = "GAMEPLAY systems", Category = DisciplineCategory.Programming,
            JobType = JobType.FullTime, WorkMode = JobWorkMode.Remote, ExperienceYears = 1,
            SkillIds = draft.Content.Skills.Select(x => x.SkillId).ToArray(), PageSize = 1 };
        var list = await service.SearchAsync(search);
        Assert.True(list.IsSuccess);
        Assert.Equal(draft.Id, Assert.Single(list.Value.Items).Id);
        Assert.Empty((await service.SearchAsync(search with { Keyword = "%" })).Value.Items);
        Assert.Empty((await service.SearchAsync(search with { Keyword = "_" })).Value.Items);
        var detail = await service.GetBySlugAsync(draft.Slug.ToUpperInvariant());
        Assert.True(detail.IsSuccess);
        Assert.Equal(3, detail.Value.Skills.Count);
        var studio = await session.Db.StudioProfiles.SingleAsync();
        studio.VerificationStatus = StudioVerificationStatus.Rejected;
        await session.Db.SaveChangesAsync();
        Assert.Empty((await service.SearchAsync(new())).Value.Items);
        Assert.Equal("PublicJobs.Unavailable", (await service.GetBySlugAsync(draft.Slug)).Error.Code);
        Assert.Equal(JobStatus.Active, (await session.Db.Jobs.SingleAsync()).Status);
        Assert.Equal(2, await session.Db.JobStatusHistory.CountAsync());
        Assert.Single(await session.Db.JobPublicationEvents.ToArrayAsync());
    }

    [PostgresFact]
    public async Task CandidateJobBlock_TranslatesToPostgresAndUnblocksImmediately()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        await using var session = h.Session();
        var published = await session.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
        Assert.True(published.IsSuccess && published.Value.Applied);
        var userId = Guid.NewGuid();
        var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
        session.Db.Cities.Add(city);
        session.Db.Users.Add(new ApplicationUser { Id = userId, AccountType = AccountType.Candidate, UserName = "candidate@example.com", Email = "candidate@example.com" });
        await session.Db.SaveChangesAsync();
        var candidate = new CandidateProfile { Id = Guid.NewGuid(), UserId = userId, CityId = city.Id, FullName = "Test Candidate", Headline = "Game developer" };
        session.Db.CandidateProfiles.Add(candidate);
        await session.Db.SaveChangesAsync();
        var block = new CandidateStudioBlock { CandidateId = candidate.Id, StudioId = h.Studio };
        session.Db.CandidateStudioBlocks.Add(block);
        await session.Db.SaveChangesAsync();
        var service = new PublicJobQueryService(session.Db, new TestClock(), null!);
        Assert.Single((await service.SearchAsync(new())).Value.Items);
        Assert.Empty((await service.SearchAsync(new(), viewerUserId: userId)).Value.Items);
        Assert.Equal("PublicJobs.Unavailable", (await service.GetBySlugAsync(draft.Slug, viewerUserId: userId)).Error.Code);
        session.Db.CandidateStudioBlocks.Remove(block);
        await session.Db.SaveChangesAsync();
        Assert.Single((await service.SearchAsync(new(), viewerUserId: userId)).Value.Items);
        Assert.True((await service.GetBySlugAsync(draft.Slug, viewerUserId: userId)).IsSuccess);
    }

    [PostgresFact]
    public async Task SkillOnlyEdit_AdvancesParentXmin_AndRejectsOldSelection()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        await using var first = h.Session();
        var skills = draft.Content.Skills.Select(x => x with { MinProficiency = SkillProficiency.Expert }).ToArray();
        var updated = await first.Service.SaveAsync(new(h.Owner, h.Studio, draft.Content with { Skills = skills }, draft.Id, draft.Version));
        Assert.True(updated.IsSuccess);
        Assert.NotEqual(draft.Version, updated.Value.Version);
        await using var second = h.Session();
        var stale = await second.Service.SaveAsync(new(h.Owner, h.Studio, draft.Content, draft.Id, draft.Version));
        Assert.False(stale.IsSuccess);
        Assert.Equal("Jobs.StaleVersion", stale.Error.Code);
        Assert.All(await second.Db.JobSkills.AsNoTracking().ToArrayAsync(), x => Assert.Equal(SkillProficiency.Expert, x.MinProficiency));
    }

    [PostgresFact]
    public async Task ConcurrentDuplication_RespectsDraftQuota_WithoutChangingOriginalHistory()
    {
        await using var h = await DatabaseHarness.Create();
        var source = await h.Draft("Gameplay Developer Alpha");
        await using (var publish = h.Session())
        {
            var published = await publish.Service.TransitionAsync(new(h.Owner, h.Studio, source.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess);
            Assert.True(published.Value.Applied);
        }
        using var gate = new SemaphoreSlim(0, 2);
        async Task<bool> Copy()
        {
            await using var session = h.Session(new JobLifecycleOptions { MaxDraftsPerStudio = 1 });
            await session.Db.Database.OpenConnectionAsync();
            await gate.WaitAsync();
            var result = await session.Service.DuplicateAsync(h.Owner, h.Studio, source.Id);
            if (result.IsFailure) Assert.Equal("Jobs.DraftLimit", result.Error.Code);
            return result.IsSuccess;
        }
        var first = Copy(); var second = Copy(); gate.Release(2);
        var outcomes = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Single(outcomes.Where(x => x));
        await using var verify = h.Session();
        Assert.Equal(1, await verify.Db.Jobs.CountAsync(x => x.Status == JobStatus.Draft));
        Assert.Equal(2, await verify.Db.JobStatusHistory.CountAsync(x => x.JobId == source.Id));
        Assert.Single(await verify.Db.JobPublicationEvents.ToArrayAsync());
    }

    [PostgresFact]
    public async Task ConcurrentPublication_RespectsActiveSlotLimit()
    {
        await using var h = await DatabaseHarness.Create();
        var first = await h.Draft("Gameplay Developer Alpha");
        var second = await h.Draft("Gameplay Developer Beta");
        var limits = new JobLifecycleOptions { FreeActiveJobs = 1 };
        var outcomes = await Race(h, first, second, limits);
        Assert.Single(outcomes.Where(x => x.Applied));
        Assert.Contains(outcomes.SelectMany(x => x.Findings), x => x.Code == "Jobs.ActiveLimit");
        await AssertPublicationState(h, 1);
    }

    [PostgresFact]
    public async Task ConcurrentPublication_RespectsRollingPublicationQuota()
    {
        await using var h = await DatabaseHarness.Create();
        var first = await h.Draft("Gameplay Developer Alpha");
        var second = await h.Draft("Gameplay Developer Beta");
        var outcomes = await Race(h, first, second, new JobLifecycleOptions { PublicationsPer24Hours = 1 });
        Assert.Single(outcomes.Where(x => x.Applied));
        Assert.Contains(outcomes.SelectMany(x => x.Findings), x => x.Code == "Jobs.PostingLimit");
        await AssertPublicationState(h, 1);
    }

    [PostgresFact]
    public async Task ConcurrentIdenticalPublication_HasOnlyOneWinner()
    {
        await using var h = await DatabaseHarness.Create();
        var first = await h.Draft("Unity Gameplay Developer");
        var second = await h.Draft("Unity Gameplay Developer");
        var outcomes = await Race(h, first, second, new JobLifecycleOptions());
        Assert.Single(outcomes.Where(x => x.Applied));
        Assert.Contains(outcomes.SelectMany(x => x.Findings), x => x.Code == "Jobs.Duplicate");
        await AssertPublicationState(h, 1);
    }

    [PostgresFact]
    public async Task StaleXminEdit_DoesNotOverwriteNewerContent()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Unity Gameplay Developer");
        await using var first = h.Session();
        await using var second = h.Session();
        var saved = await first.Service.SaveAsync(new(h.Owner, h.Studio,
            draft.Content with { Title = "New Gameplay Developer" }, draft.Id, draft.Version));
        Assert.True(saved.IsSuccess, saved.Error.Message);
        Assert.NotEqual(draft.Version, saved.Value.Version);
        var stale = await second.Service.SaveAsync(new(h.Owner, h.Studio,
            draft.Content with { Title = "Stale Gameplay Developer" }, draft.Id, draft.Version));
        Assert.False(stale.IsSuccess);
        Assert.Equal("Jobs.StaleVersion", stale.Error.Code);
        await using var verify = h.Session();
        Assert.Equal("New Gameplay Developer", (await verify.Db.Jobs.SingleAsync()).Title);
        Assert.Single(await verify.Db.JobStatusHistory.ToArrayAsync());
    }

    [PostgresFact]
    public async Task FailureAfterSave_RollsBackJobHistoryAndPublicationEventTogether()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Unity Gameplay Developer");
        await using (var failing = h.Session(interceptor: new FailAfterSave()))
        {
            await Assert.ThrowsAsync<InjectedFailure>(() => failing.Service.TransitionAsync(
                new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish)));
        }
        await AssertPublicationState(h, 0);
        await using var retry = h.Session();
        var result = await retry.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.True(result.Value.Applied);
        await AssertPublicationState(h, 1);
    }

    private static async Task<JobLifecycleOutcome[]> Race(DatabaseHarness h, JobSnapshot a, JobSnapshot b, JobLifecycleOptions limits)
    {
        // Independent connections/DbContexts, released from a common start gate.
        using var gate = new SemaphoreSlim(0, 2);
        async Task<JobLifecycleOutcome> Publish(JobSnapshot job)
        {
            await using var session = h.Session(limits);
            await session.Db.Database.OpenConnectionAsync();
            await gate.WaitAsync();
            var result = await session.Service.TransitionAsync(new(h.Owner, h.Studio, job.Id, JobLifecycleAction.Publish));
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value;
        }
        var first = Publish(a);
        var second = Publish(b);
        gate.Release(2);
        return await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static async Task AssertPublicationState(DatabaseHarness h, int published)
    {
        await using var session = h.Session();
        Assert.Equal(published, await session.Db.Jobs.CountAsync(x => x.Status == JobStatus.Active));
        Assert.Equal(published, await session.Db.JobPublicationEvents.CountAsync());
        Assert.Equal(published, await session.Db.JobStatusHistory.CountAsync(x => x.NewStatus == JobStatus.Active));
        Assert.Equal(await session.Db.Jobs.CountAsync(), await session.Db.JobStatusHistory.CountAsync(x => x.NewStatus == JobStatus.Draft));
        Assert.Equal(await session.Db.Jobs.CountAsync() + published, await session.Db.JobStatusHistory.CountAsync());
    }

    private sealed class InjectedFailure : Exception { }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InjectedFailure();
    }

    private sealed class DatabaseHarness : IAsyncDisposable
    {
        private readonly string _admin;
        private readonly string _name = "hgd_test_" + Guid.NewGuid().ToString("N");
        private string Connection => new NpgsqlConnectionStringBuilder(_admin) { Database = _name, Pooling = false }.ConnectionString;
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Studio { get; } = Guid.NewGuid();
        private int[] _skills = Array.Empty<int>();
        private DatabaseHarness(string admin) => _admin = admin;

        public static async Task<DatabaseHarness> Create()
        {
            var h = new DatabaseHarness(Environment.GetEnvironmentVariable("HGD_TEST_POSTGRES")!);
            await using (var connection = new NpgsqlConnection(h._admin))
            {
                await connection.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{h._name}\"", connection);
                await create.ExecuteNonQueryAsync();
            }
            try
            {
                await using var session = h.Session();
                // Exercise checked-in migrations, constraints and xmin, not EnsureCreated/InMemory.
                await session.Db.Database.MigrateAsync();
                session.Db.Users.Add(new ApplicationUser { Id = h.Owner, AccountType = AccountType.Studio,
                    UserName = "studio@example.com", Email = "studio@example.com" });
                session.Db.StudioProfiles.Add(new StudioProfile { Id = h.Studio, OwnerUserId = h.Owner,
                    Name = "Integration Studio", Slug = "integration-studio", VerificationStatus = StudioVerificationStatus.Verified });
                session.Db.StudioMembers.Add(new StudioMember { StudioId = h.Studio, UserId = h.Owner, MemberRole = StudioMemberRole.Owner });
                var skills = Enumerable.Range(1, 3).Select(i => new Skill { Name = $"Integration Skill {i}", Slug = $"integration-skill-{i}" }).ToArray();
                session.Db.Skills.AddRange(skills);
                await session.Db.SaveChangesAsync();
                h._skills = skills.Select(x => x.Id).ToArray();
                return h;
            }
            catch { await h.DisposeAsync(); throw; }
        }

        public Session Session(JobLifecycleOptions? limits = null, IInterceptor? interceptor = null) => new(Connection, limits, interceptor);
        public async Task<JobSnapshot> Draft(string title)
        {
            await using var session = Session();
            var content = JobPublishRulesTests.Valid() with { Title = title,
                Skills = _skills.Select(id => new JobSkillInput(id, true, SkillProficiency.Working)).ToArray() };
            var result = await session.Service.SaveAsync(new(Owner, Studio, content));
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value;
        }
        public async ValueTask DisposeAsync()
        {
            // Only the generated, unique database is ever dropped. Never delete the supplied database.
            await using var connection = new NpgsqlConnection(_admin);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class Session : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public JobLifecycleService Service { get; }
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public Session(string connection, JobLifecycleOptions? limits, IInterceptor? interceptor)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention();
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var clock = new TestClock();
            Db = new(options.Options, clock);
            var policy = Options.Create(limits ?? new JobLifecycleOptions());
            var audit = new NullAudit();
            var checker = new JobPublishChecker(Db, new SettingsService(Db, _cache, audit, clock), new JobDuplicateDetector(Db, clock, policy));
            Service = new(Db, clock, checker, audit, policy);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); _cache.Dispose(); }
    }
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class NullAudit : IAuditLogger
    {
        public void Enqueue(AuditLogEntry entry) { }
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
