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
