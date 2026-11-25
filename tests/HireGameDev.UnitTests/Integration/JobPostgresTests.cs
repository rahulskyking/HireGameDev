using System.Data.Common;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Application.Features.Applications;
using Microsoft.AspNetCore.DataProtection;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using HireGameDev.UnitTests.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public async Task ApplicationSubmission_RejectsStaleAndBlockedReviews_AndEnforcesDuplicateAndCapacityRaces()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var candidates = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            for (var i = 0; i < users.Length; i++)
                setup.Db.Users.Add(new ApplicationUser { Id = users[i], AccountType = AccountType.Candidate, UserName = $"apply{i}@example.com", Email = $"apply{i}@example.com" });
            await setup.Db.SaveChangesAsync();
            for (var i = 0; i < users.Length; i++)
                setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidates[i], UserId = users[i], CityId = city.Id, FullName = $"Candidate {i}", Headline = "Game developer" });
            var job = await setup.Db.Jobs.SingleAsync(x => x.Id == draft.Id);
            job.ApplicationCap = 2;
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        ApplicationSubmissionService Submission(AppDbContext db)
        {
            var clock = new TestClock();
            return new ApplicationSubmissionService(db, new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        }
        async Task<ApplicationReviewOffer> Offer(Guid user)
        {
            await using var session = h.Session();
            var result = await Submission(session.Db).PrepareAsync(user, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value;
        }
        async Task<Result<SubmittedApplication>> Submit(Guid user, string token, Task? gate = null)
        {
            await using var session = h.Session();
            if (gate is not null) await gate;
            return await Submission(session.Db).SubmitAsync(new(user, draft.Id, null, token, true));
        }
        var stale = await Offer(users[0]);
        await using (var invalid = h.Session())
        {
            var service = Submission(invalid.Db);
            Assert.Equal("Applications.ConsentRequired", (await service.SubmitAsync(new(users[0], draft.Id, null, stale.ReviewToken, false))).Error.Code);
            Assert.Equal("Applications.ReviewStale", (await service.SubmitAsync(new(users[1], draft.Id, null, stale.ReviewToken, true))).Error.Code);
            Assert.Equal("Applications.ReviewStale", (await service.SubmitAsync(new(users[0], draft.Id, null, "not-a-token", true))).Error.Code);
        }
        await using (var edit = h.Session())
        {
            var candidate = await edit.Db.CandidateProfiles.SingleAsync(x => x.Id == candidates[0]);
            candidate.Headline = "Updated developer";
            await edit.Db.SaveChangesAsync();
        }
        Assert.Equal("Applications.ReviewStale", (await Submit(users[0], stale.ReviewToken)).Error.Code);
        var blocked = await Offer(users[0]);
        await using (var edit = h.Session())
        {
            edit.Db.CandidateStudioBlocks.Add(new CandidateStudioBlock { CandidateId = candidates[0], StudioId = h.Studio });
            await edit.Db.SaveChangesAsync();
        }
        Assert.Equal("Applications.Unavailable", (await Submit(users[0], blocked.ReviewToken)).Error.Code);
        await using (var edit = h.Session())
        {
            edit.Db.CandidateStudioBlocks.Remove(await edit.Db.CandidateStudioBlocks.SingleAsync());
            await edit.Db.SaveChangesAsync();
        }
        var one = await Offer(users[0]);
        var two = await Offer(users[1]);
        var three = await Offer(users[2]);
        var duplicateGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var d1 = Submit(users[0], one.ReviewToken, duplicateGate.Task);
        var d2 = Submit(users[0], one.ReviewToken, duplicateGate.Task);
        duplicateGate.SetResult(true);
        var duplicate = await Task.WhenAll(d1, d2).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Single(duplicate.Where(x => x.IsSuccess));
        var capacityGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var c1 = Submit(users[1], two.ReviewToken, capacityGate.Task);
        var c2 = Submit(users[2], three.ReviewToken, capacityGate.Task);
        capacityGate.SetResult(true);
        var capacity = await Task.WhenAll(c1, c2).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Single(capacity.Where(x => x.IsSuccess));
        await using var check = h.Session();
        var finalJob = await check.Db.Jobs.SingleAsync(x => x.Id == draft.Id);
        Assert.Equal(2, finalJob.ApplicationsCount);
        Assert.Equal(JobStatus.Full, finalJob.Status);
        Assert.Equal(2, await check.Db.Applications.CountAsync());
        Assert.Equal(2, await check.Db.ApplicationSnapshots.CountAsync());
        var receipts = await check.Db.Applications.ToArrayAsync();
        Assert.All(receipts, receipt =>
        {
            Assert.Equal(ApplicationSubmissionService.ConsentVersion, receipt.ConsentVersion);
            Assert.Equal(receipt.SubmittedAt, receipt.ConsentedAt);
            Assert.Null(receipt.SnapshotPurgedAt);
        });
        Assert.Equal(2, await check.Db.ApplicationStatusHistory.CountAsync());
        Assert.Equal(1, await check.Db.JobStatusHistory.CountAsync(x => x.NewStatus == JobStatus.Full));
    }

    [PostgresFact]
    public async Task RetentionMetadataMigration_BackfillsExistingReceiptsWithoutDeletingSnapshots()
    {
        await using var h = await DatabaseHarness.Create();
        await using (var migration = h.Session())
            await migration.Db.GetService<IMigrator>().MigrateAsync("20261116090000_CandidateApplicationCoordination");
        var draft = await h.Draft("Gameplay Developer Alpha");
        var user = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var application = Guid.NewGuid();
        var recorded = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        await using (var setup = h.Session())
        {
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "migration@example.com", Email = "migration@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidate, UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
            await setup.Db.SaveChangesAsync();
            await setup.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO applications (id, candidate_id, job_id, status, submitted_at, updated_at) VALUES ({application}, {candidate}, {draft.Id}, 'submitted', {recorded}, {recorded})");
            var candidateJson = "{\"name\":\"Candidate\"}";
            var jobJson = "{}";
            await setup.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO application_snapshots (application_id, snapshot_version, candidate_data, job_data, phone, consent_version, consented_at) VALUES ({application}, 1, CAST({candidateJson} AS jsonb), CAST({jobJson} AS jsonb), {"1234567890"}, {"legacy-v1"}, {recorded})");
        }
        await using (var migration = h.Session())
            await migration.Db.GetService<IMigrator>().MigrateAsync();
        await using var check = h.Session();
        var receipt = await check.Db.Applications.SingleAsync(x => x.Id == application);
        var snapshot = await check.Db.ApplicationSnapshots.SingleAsync(x => x.ApplicationId == application);
        Assert.Equal("legacy-v1", receipt.ConsentVersion);
        Assert.Equal(recorded, receipt.ConsentedAt);
        Assert.Null(receipt.SnapshotPurgedAt);
        Assert.Equal("legacy-v1", snapshot.ConsentVersion);
        Assert.Equal("1234567890", snapshot.Phone);
        Assert.Contains("Candidate", snapshot.CandidateData);
    }

    [PostgresFact]
    public async Task ApplicationSubmission_RealDeadlock_RollsBackVictimAndKeepsReviewedSnapshot()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var user = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var skill = new Skill { Name = "Deadlock Skill", Slug = "deadlock-skill" };
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Skills.Add(skill);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "deadlock@example.com", Email = "deadlock@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidate, UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
            (await setup.Db.Jobs.SingleAsync(x => x.Id == draft.Id)).ApplicationCap = 1;
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateSkills.Add(new CandidateSkill { CandidateId = candidate, SkillId = skill.Id });
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        ApplicationSubmissionService Submission(AppDbContext db) => new(db,
            new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        async Task<ApplicationReviewOffer> Offer()
        {
            await using var session = h.Session();
            var result = await Submission(session.Db).PrepareAsync(user, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value;
        }
        var original = await Offer();
        await using var writer = h.Session();
        await using var writerTx = await writer.Db.Database.BeginTransactionAsync();
        await writer.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE skills SET name = {"Changed Skill"} WHERE id = {skill.Id}");
        var writerPid = ((NpgsqlConnection)writer.Db.Database.GetDbConnection()).ProcessID;
        var gate = new PauseSkillLock();
        await using var submitting = h.Session(interceptor: gate);
        var pendingSubmit = Submission(submitting.Db).SubmitAsync(new(user, draft.Id, null, original.ReviewToken, true));
        Task<bool>? pendingWriter = null;
        try
        {
            // Submission holds the Candidate row and is paused just before requesting the skill.
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            async Task<bool> ChangeCandidate()
            {
                try
                {
                    await writer.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE candidate_profiles SET headline = {"Changed Developer"} WHERE id = {candidate}");
                    await writerTx.CommitAsync();
                    return true;
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DeadlockDetected)
                {
                    await writerTx.RollbackAsync();
                    return false;
                }
            }
            pendingWriter = ChangeCandidate();
            // Observe a real lock wait before completing the cycle. This test runs on the same
            // disposable server/account and does not depend on a guessed sleep duration.
            await using var monitor = h.Session();
            await monitor.Db.Database.OpenConnectionAsync();
            var waiting = false;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline && !waiting && !pendingWriter.IsCompleted)
            {
                await using var cmd = monitor.Db.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @pid AND wait_event_type = 'Lock')";
                var parameter = cmd.CreateParameter(); parameter.ParameterName = "pid"; parameter.Value = writerPid; cmd.Parameters.Add(parameter);
                waiting = (bool)(await cmd.ExecuteScalarAsync())!;
                if (!waiting) await Task.Delay(50);
            }
            Assert.True(waiting, "The competing writer did not wait for the Candidate lock.");
        }
        finally { gate.Release.TrySetResult(true); }
        var submitted = await pendingSubmit.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(pendingWriter);
        var writerCommitted = await pendingWriter!.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(writerCommitted, submitted.IsFailure);
        if (writerCommitted)
        {
            Assert.Equal("Applications.ConcurrentChange", submitted.Error.Code);
            await using (var check = h.Session())
            {
                Assert.Empty(await check.Db.Applications.ToArrayAsync());
                Assert.Empty(await check.Db.ApplicationSnapshots.ToArrayAsync());
                Assert.Empty(await check.Db.ApplicationStatusHistory.ToArrayAsync());
                var job = await check.Db.Jobs.SingleAsync(x => x.Id == draft.Id);
                Assert.Equal(0, job.ApplicationsCount);
                Assert.Equal(JobStatus.Active, job.Status);
            }
            var fresh = await Offer();
            await using var retry = h.Session();
            var retried = await Submission(retry.Db).SubmitAsync(new(user, draft.Id, null, fresh.ReviewToken, true));
            Assert.True(retried.IsSuccess, retried.Error.Message);
        }
        await using (var check = h.Session())
        {
            Assert.Single(await check.Db.Applications.ToArrayAsync());
            Assert.Single(await check.Db.ApplicationSnapshots.ToArrayAsync());
            Assert.Single(await check.Db.ApplicationStatusHistory.ToArrayAsync());
            var job = await check.Db.Jobs.SingleAsync(x => x.Id == draft.Id);
            Assert.Equal(1, job.ApplicationsCount);
            Assert.Equal(JobStatus.Full, job.Status);
            Assert.Equal(1, await check.Db.JobStatusHistory.CountAsync(x => x.JobId == draft.Id && x.NewStatus == JobStatus.Full));
            var snapshot = await check.Db.ApplicationSnapshots.SingleAsync();
            Assert.Contains(writerCommitted ? "Changed Developer" : "Developer", snapshot.CandidateData);
            Assert.Contains(writerCommitted ? "Changed Skill" : "Deadlock Skill", snapshot.CandidateData);
        }
    }

    [PostgresFact]
    public async Task ApplicationSubmission_RejectsReviewWhenReferencedSkillIsRenamed()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var user = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var skill = new Skill { Name = "Reference Skill", Slug = "reference-skill" };
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Skills.Add(skill);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "rename@example.com", Email = "rename@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidate, UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateSkills.Add(new CandidateSkill { CandidateId = candidate, SkillId = skill.Id });
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        ApplicationSubmissionService Submission(AppDbContext db) => new(db,
            new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        ApplicationReviewOffer offer;
        await using (var prepare = h.Session())
        {
            var result = await Submission(prepare.Db).PrepareAsync(user, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            offer = result.Value;
        }
        await using var edit = h.Session();
        await using var tx = await edit.Db.Database.BeginTransactionAsync();
        await edit.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE skills SET name = {"Renamed Skill"} WHERE id = {skill.Id}");
        await using var submit = h.Session();
        var pending = Submission(submit.Db).SubmitAsync(new(user, draft.Id, null, offer.ReviewToken, true));
        await tx.CommitAsync();
        var resultAfterEdit = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(resultAfterEdit.IsFailure);
        Assert.Equal("Applications.ReviewStale", resultAfterEdit.Error.Code);
        await using var check = h.Session();
        Assert.Empty(await check.Db.Applications.ToArrayAsync());
    }

    [PostgresFact]
    public async Task ApplicationSubmission_LocksOnlyReferencedCityAndAllReferencedSkills()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var user = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var portfolio = Guid.NewGuid();
        var cities = new[] { new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" },
            new City { Name = "Other City", Slug = "other-city", State = "Test State", StateCode = "TS" } };
        var skills = Enumerable.Range(1, 4).Select(i => new Skill { Name = $"Reference Skill {i}", Slug = $"reference-skill-{i}" }).ToArray();
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            setup.Db.Cities.AddRange(cities);
            setup.Db.Skills.AddRange(skills);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "reference@example.com", Email = "reference@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidate, UserId = user, CityId = cities[0].Id, FullName = "Candidate", Headline = "Developer" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateSkills.Add(new CandidateSkill { CandidateId = candidate, SkillId = skills[0].Id });
            setup.Db.PortfolioLinks.Add(new PortfolioLink { Id = portfolio, CandidateId = candidate, Url = "https://example.com/project" });
            setup.Db.ShippedTitles.Add(new ShippedTitle { Id = Guid.NewGuid(), CandidateId = candidate, GameName = "Test Game", StudioName = "Test Studio", Role = "Developer", EngineSkillId = skills[2].Id });
            await setup.Db.SaveChangesAsync();
            setup.Db.PortfolioLinkSkills.Add(new PortfolioLinkSkill { PortfolioLinkId = portfolio, SkillId = skills[1].Id });
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        ApplicationSubmissionService Submission(AppDbContext db) => new(db,
            new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        ApplicationReviewOffer offer;
        await using (var prepare = h.Session())
        {
            var result = await Submission(prepare.Db).PrepareAsync(user, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            offer = result.Value;
        }
        var gate = new PauseEligibilityRead();
        await using var submitting = h.Session(interceptor: gate);
        var pending = Submission(submitting.Db).SubmitAsync(new(user, draft.Id, null, offer.ReviewToken, true));
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using (var other = h.Session())
            {
                await other.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cities SET name = {"Other City Updated"} WHERE id = {cities[1].Id}");
                await other.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE skills SET name = {"Other Skill Updated"} WHERE id = {skills[3].Id}");
            }
            async Task AssertBlocked(bool city, int id)
            {
                await using var writer = h.Session();
                await using var tx = await writer.Db.Database.BeginTransactionAsync();
                await writer.Db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '500ms'");
                var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                {
                    if (city) await writer.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cities SET name = {"Changed City"} WHERE id = {id}");
                    else await writer.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE skills SET name = {"Changed Skill"} WHERE id = {id}");
                });
                Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
                await tx.RollbackAsync();
            }
            await AssertBlocked(true, cities[0].Id);
            foreach (var skill in skills.Take(3)) await AssertBlocked(false, skill.Id);
        }
        finally { gate.Release.TrySetResult(true); }
        var submitted = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(submitted.IsSuccess, submitted.Error.Message);
    }

    [PostgresFact]
    public async Task ApplicationSubmission_RechecksAccountAndStudioChangesCommittedDuringSubmission()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            for (var i = 0; i < users.Length; i++)
                setup.Db.Users.Add(new ApplicationUser { Id = users[i], AccountType = AccountType.Candidate, UserName = $"recheck{i}@example.com", Email = $"recheck{i}@example.com" });
            await setup.Db.SaveChangesAsync();
            for (var i = 0; i < users.Length; i++)
                setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = Guid.NewGuid(), UserId = users[i], CityId = city.Id, FullName = $"Candidate {i}", Headline = "Developer" });
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        ApplicationSubmissionService Submission(AppDbContext db) => new(db,
            new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        async Task<ApplicationReviewOffer> Offer(Guid user)
        {
            await using var session = h.Session();
            var result = await Submission(session.Db).PrepareAsync(user, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            return result.Value;
        }
        var accountOffer = await Offer(users[0]);
        var studioOffer = await Offer(users[1]);
        await using (var edit = h.Session())
        await using (var tx = await edit.Db.Database.BeginTransactionAsync())
        await using (var submit = h.Session())
        {
            await edit.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET is_active = false WHERE id = {users[0]}");
            var pending = Submission(submit.Db).SubmitAsync(new(users[0], draft.Id, null, accountOffer.ReviewToken, true));
            await tx.CommitAsync();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(result.IsFailure);
            Assert.Equal("Applications.CandidateRequired", result.Error.Code);
        }
        await using (var edit = h.Session())
        await using (var tx = await edit.Db.Database.BeginTransactionAsync())
        await using (var submit = h.Session())
        {
            await edit.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE studio_profiles SET verification_status = {"rejected"} WHERE id = {h.Studio}");
            var pending = Submission(submit.Db).SubmitAsync(new(users[1], draft.Id, null, studioOffer.ReviewToken, true));
            await tx.CommitAsync();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(result.IsFailure);
            Assert.Equal("Applications.Unavailable", result.Error.Code);
        }
        await using var check = h.Session();
        Assert.Empty(await check.Db.Applications.ToArrayAsync());
    }

    [PostgresFact]
    public async Task ApplicationSubmission_LocksOnlyRelevantAccountAndStudio()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var user = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var otherStudio = Guid.NewGuid();
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "account@example.com", Email = "account@example.com" });
            setup.Db.Users.Add(new ApplicationUser { Id = otherUser, AccountType = AccountType.Studio, UserName = "other@example.com", Email = "other@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = Guid.NewGuid(), UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
            setup.Db.StudioProfiles.Add(new StudioProfile { Id = otherStudio, OwnerUserId = otherUser, Name = "Other Studio", Slug = "other-studio" });
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        ApplicationSubmissionService Submission(AppDbContext db) => new(db,
            new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        ApplicationReviewOffer offer;
        await using (var prepare = h.Session())
        {
            var result = await Submission(prepare.Db).PrepareAsync(user, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            offer = result.Value;
        }
        var gate = new PauseEligibilityRead();
        await using var submitting = h.Session(interceptor: gate);
        var pending = Submission(submitting.Db).SubmitAsync(new(user, draft.Id, null, offer.ReviewToken, true));
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using (var other = h.Session())
            {
                await other.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET is_active = false WHERE id = {otherUser}");
                await other.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE studio_profiles SET name = {"Other Studio Updated"} WHERE id = {otherStudio}");
            }
            async Task AssertBlocked(string table)
            {
                await using var same = h.Session();
                await using var tx = await same.Db.Database.BeginTransactionAsync();
                await same.Db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '500ms'");
                var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                {
                    if (table == "user")
                        await same.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET is_active = false WHERE id = {user}");
                    else
                        await same.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE studio_profiles SET name = {"Changed"} WHERE id = {h.Studio}");
                });
                Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
                await tx.RollbackAsync();
            }
            await AssertBlocked("user");
            await AssertBlocked("studio");
        }
        finally { gate.Release.TrySetResult(true); }
        var submitted = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(submitted.IsSuccess, submitted.Error.Message);
    }

    [PostgresFact]
    public async Task ApplicationSubmission_SeesCommittedChildEditBeforeAcceptingReview()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var user = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "edit@example.com", Email = "edit@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidate, UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        ApplicationSubmissionService Submission(AppDbContext db) => new(db,
            new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        ApplicationReviewOffer offer;
        await using (var prepare = h.Session())
        {
            var result = await Submission(prepare.Db).PrepareAsync(user, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            offer = result.Value;
        }
        await using var edit = h.Session();
        await using var transaction = await edit.Db.Database.BeginTransactionAsync();
        edit.Db.CandidateEducation.Add(new CandidateEducation { Id = Guid.NewGuid(), CandidateId = candidate, Institution = "Test University", Degree = "Game Design" });
        await edit.Db.SaveChangesAsync(); // Trigger holds the Candidate lock until commit.
        await using var submit = h.Session();
        var pending = Submission(submit.Db).SubmitAsync(new(user, draft.Id, null, offer.ReviewToken, true));
        await transaction.CommitAsync();
        var resultAfterEdit = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(resultAfterEdit.IsFailure);
        Assert.Equal("Applications.ReviewStale", resultAfterEdit.Error.Code);
        await using var check = h.Session();
        Assert.Empty(await check.Db.Applications.ToArrayAsync());
    }

    [PostgresFact]
    public async Task CandidateApplicationLock_BlocksOwnChildWritesButNotOtherCandidates()
    {
        await using var h = await DatabaseHarness.Create();
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var candidates = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var setup = h.Session())
        {
            var city = new City { Name = "Locktown", Slug = "locktown", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            for (var i = 0; i < users.Length; i++)
                setup.Db.Users.Add(new ApplicationUser { Id = users[i], AccountType = AccountType.Candidate,
                    UserName = $"lock{i}@example.com", Email = $"lock{i}@example.com" });
            await setup.Db.SaveChangesAsync();
            for (var i = 0; i < users.Length; i++)
                setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidates[i], UserId = users[i], CityId = city.Id,
                    FullName = $"Candidate {i}", Headline = "Developer" });
            await setup.Db.SaveChangesAsync();
        }
        await using var holder = h.Session();
        await using var held = await holder.Db.Database.BeginTransactionAsync();
        await holder.Db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM candidate_profiles WHERE id = {candidates[0]} FOR UPDATE");
        await using (var other = h.Session())
        {
            await other.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO candidate_education (id, candidate_id, institution, degree) VALUES ({Guid.NewGuid()}, {candidates[1]}, {"Institution"}, {"Degree"})");
        }
        await using (var same = h.Session())
        {
            await using var tx = await same.Db.Database.BeginTransactionAsync();
            await same.Db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '500ms'");
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await same.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO candidate_studio_blocks (candidate_id, studio_id, created_at) VALUES ({candidates[0]}, {h.Studio}, now())"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
            await tx.RollbackAsync();
        }
        await held.CommitAsync();
        await using (var retry = h.Session())
            await retry.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO candidate_studio_blocks (candidate_id, studio_id, created_at) VALUES ({candidates[0]}, {h.Studio}, now())");
    }

    [PostgresFact]
    public async Task ApplicationSubmission_WrappedDatabaseConflicts_RollBackAndAllowRetry()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var user = Guid.NewGuid();
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "conflict@example.com", Email = "conflict@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = Guid.NewGuid(), UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
            (await setup.Db.Jobs.SingleAsync(x => x.Id == draft.Id)).ApplicationCap = 1;
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        ApplicationSubmissionService Submission(AppDbContext db) => new(db,
            new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        ApplicationReviewOffer offer;
        await using (var prepare = h.Session())
        {
            var result = await Submission(prepare.Db).PrepareAsync(user, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            offer = result.Value;
        }
        foreach (var sqlState in new[] { PostgresErrorCodes.DeadlockDetected, PostgresErrorCodes.SerializationFailure })
        {
            await using (var failing = h.Session(interceptor: new InjectWrappedConflictAfterSave(sqlState)))
            {
                var result = await Submission(failing.Db).SubmitAsync(new(user, draft.Id, null, offer.ReviewToken, true));
                Assert.True(result.IsFailure);
                Assert.Equal("Applications.ConcurrentChange", result.Error.Code);
                Assert.False(failing.Db.ChangeTracker.HasChanges());
            }
            await using var check = h.Session();
            Assert.Empty(await check.Db.Applications.ToArrayAsync());
            Assert.Empty(await check.Db.ApplicationSnapshots.ToArrayAsync());
            Assert.Empty(await check.Db.ApplicationStatusHistory.ToArrayAsync());
            var job = await check.Db.Jobs.SingleAsync(x => x.Id == draft.Id);
            Assert.Equal(0, job.ApplicationsCount);
            Assert.Equal(JobStatus.Active, job.Status);
            Assert.Equal(0, await check.Db.JobStatusHistory.CountAsync(x => x.JobId == draft.Id && x.NewStatus == JobStatus.Full));
        }
        await using (var retry = h.Session())
        {
            var result = await Submission(retry.Db).SubmitAsync(new(user, draft.Id, null, offer.ReviewToken, true));
            Assert.True(result.IsSuccess, result.Error.Message);
            Assert.Equal(JobStatus.Full, (await retry.Db.Jobs.SingleAsync(x => x.Id == draft.Id)).Status);
        }
    }

    [PostgresFact]
    public async Task ApplicationSubmission_FailureAfterSave_RollsBackApplicationAndFullTransition()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        var userId = Guid.NewGuid();
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Users.Add(new ApplicationUser { Id = userId, AccountType = AccountType.Candidate, UserName = "rollback@example.com", Email = "rollback@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = Guid.NewGuid(), UserId = userId, CityId = city.Id, FullName = "Test Candidate", Headline = "Game developer" });
            (await setup.Db.Jobs.SingleAsync(x => x.Id == draft.Id)).ApplicationCap = 1;
            await setup.Db.SaveChangesAsync();
        }
        var protector = new EphemeralDataProtectionProvider();
        var clock = new TestClock();
        ApplicationSubmissionService Submission(AppDbContext db) => new(db,
            new ApplicationReviewService(db, new ApplicationEligibilityService(db, clock)), clock, protector);
        ApplicationReviewOffer offer;
        await using (var prepare = h.Session())
        {
            var result = await Submission(prepare.Db).PrepareAsync(userId, draft.Id, null);
            Assert.True(result.IsSuccess, result.Error.Message);
            offer = result.Value;
        }
        await using (var failing = h.Session(interceptor: new FailAfterSave()))
        {
            await Assert.ThrowsAsync<InjectedFailure>(async () =>
                await Submission(failing.Db).SubmitAsync(new(userId, draft.Id, null, offer.ReviewToken, true)));
        }
        await using (var check = h.Session())
        {
            Assert.Empty(await check.Db.Applications.ToArrayAsync());
            Assert.Empty(await check.Db.ApplicationSnapshots.ToArrayAsync());
            Assert.Empty(await check.Db.ApplicationStatusHistory.ToArrayAsync());
            Assert.Equal(0, (await check.Db.Jobs.SingleAsync(x => x.Id == draft.Id)).ApplicationsCount);
            Assert.Equal(JobStatus.Active, (await check.Db.Jobs.SingleAsync(x => x.Id == draft.Id)).Status);
            Assert.Equal(0, await check.Db.JobStatusHistory.CountAsync(x => x.JobId == draft.Id && x.NewStatus == JobStatus.Full));
        }
        await using (var retry = h.Session())
        {
            var result = await Submission(retry.Db).SubmitAsync(new(userId, draft.Id, null, offer.ReviewToken, true));
            Assert.True(result.IsSuccess, result.Error.Message);
            Assert.Equal(JobStatus.Full, (await retry.Db.Jobs.SingleAsync(x => x.Id == draft.Id)).Status);
        }
    }

    [PostgresFact]
    public async Task ApplicationReview_LoadsPrivateSnapshotAndRespectsBlocklist()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Gameplay Developer Alpha");
        await using var session = h.Session();
        var published = await session.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
        Assert.True(published.IsSuccess && published.Value.Applied);
        var userId = Guid.NewGuid();
        var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
        session.Db.Cities.Add(city);
        session.Db.Users.Add(new ApplicationUser { Id = userId, AccountType = AccountType.Candidate, UserName = "review@example.com", Email = "review@example.com" });
        await session.Db.SaveChangesAsync();
        var candidate = new CandidateProfile { Id = Guid.NewGuid(), UserId = userId, CityId = city.Id, FullName = "Test Candidate", Headline = "Game developer", CurrentCtcLpa = 10, ShowCurrentCtc = false };
        session.Db.CandidateProfiles.Add(candidate);
        session.Db.ShippedTitles.Add(new ShippedTitle { Id = Guid.NewGuid(), CandidateId = candidate.Id, GameName = "Secret game", StudioName = "Secret studio", Role = "Developer", IsNda = true, HideStudioInPublic = true, Platforms = new[] { "pc" } });
        await session.Db.SaveChangesAsync();
        var clock = new TestClock();
        var eligibility = new ApplicationEligibilityService(session.Db, clock);
        var review = new ApplicationReviewService(session.Db, eligibility);
        var result = await review.ReviewAsync(userId, draft.Id, null);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Candidate.CurrentCtcLpa);
        Assert.Equal("Confidential Project (Under NDA)", Assert.Single(result.Value.Candidate.ShippedGames).GameName);
        Assert.Empty(await session.Db.Applications.ToArrayAsync());
        session.Db.CandidateStudioBlocks.Add(new CandidateStudioBlock { CandidateId = candidate.Id, StudioId = h.Studio });
        await session.Db.SaveChangesAsync();
        var blocked = await review.ReviewAsync(userId, draft.Id, null);
        Assert.True(blocked.IsFailure);
        Assert.Equal("Applications.Unavailable", blocked.Error.Code);
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
    public async Task SavedJobs_ConcurrentIdempotency_Privacy_BlockAndDraftDeletion()
    {
        await using var h = await DatabaseHarness.Create();
        var publishedJob = await h.Draft("Gameplay Developer Alpha");
        var otherDraft = await h.Draft("Audio Developer Beta");
        var userId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, publishedJob.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Users.Add(new ApplicationUser { Id = userId, AccountType = AccountType.Candidate, UserName = "save-candidate@example.com", Email = "save-candidate@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidateId, UserId = userId, CityId = city.Id, FullName = "Test Candidate", Headline = "Game developer" });
            // A legacy opposite profile must not grant a Studio-primary user access.
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = Guid.NewGuid(), UserId = h.Owner, CityId = city.Id, FullName = "Studio Owner", Headline = "Studio developer" });
            await setup.Db.SaveChangesAsync();
        }
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Result> SaveInIndependentContext()
        {
            await using var session = h.Session();
            var service = new SavedJobService(session.Db, new PublicJobQueryService(session.Db, new TestClock(), null!), new TestClock());
            await gate.Task;
            return await service.SaveAsync(userId, publishedJob.Id);
        }
        var firstSave = SaveInIndependentContext();
        var secondSave = SaveInIndependentContext();
        gate.SetResult(true);
        var concurrent = await Task.WhenAll(firstSave, secondSave);
        Assert.All(concurrent, result => Assert.True(result.IsSuccess));
        await using var check = h.Session();
        var saves = new SavedJobService(check.Db, new PublicJobQueryService(check.Db, new TestClock(), null!), new TestClock());
        Assert.Equal(1, await check.Db.CandidateSavedJobs.CountAsync());
        Assert.True((await saves.SaveAsync(userId, publishedJob.Id)).IsSuccess);
        Assert.Equal("SavedJobs.Forbidden", (await saves.SaveAsync(h.Owner, publishedJob.Id)).Error.Code);
        Assert.Equal("SavedJobs.Forbidden", (await saves.ListAsync(h.Owner)).Error.Code);
        Assert.Equal("SavedJobs.Forbidden", (await saves.RemoveAsync(h.Owner, publishedJob.Id)).Error.Code);
        Assert.Equal("SavedJobs.Unavailable", (await saves.SaveAsync(userId, otherDraft.Id)).Error.Code);
        var deleted = await check.Service.DeleteDraftAsync(h.Owner, h.Studio, otherDraft.Id);
        Assert.True(deleted.IsSuccess);
        Assert.Equal(publishedJob.Id, Assert.Single((await saves.ListAsync(userId)).Value.Items).Job!.Id);
        var block = new CandidateStudioBlock { CandidateId = candidateId, StudioId = h.Studio };
        check.Db.CandidateStudioBlocks.Add(block); await check.Db.SaveChangesAsync();
        Assert.Empty((await saves.ListAsync(userId)).Value.Items);
        Assert.Equal("SavedJobs.Unavailable", (await saves.SaveAsync(userId, publishedJob.Id)).Error.Code);
        check.Db.CandidateStudioBlocks.Remove(block); await check.Db.SaveChangesAsync();
        Assert.NotNull(Assert.Single((await saves.ListAsync(userId)).Value.Items).Job);
        var studio = await check.Db.StudioProfiles.SingleAsync();
        studio.VerificationStatus = StudioVerificationStatus.Rejected; await check.Db.SaveChangesAsync();
        var unavailable = Assert.Single((await saves.ListAsync(userId)).Value.Items);
        Assert.Null(unavailable.Job);
        var json = System.Text.Json.JsonSerializer.Serialize(unavailable);
        Assert.DoesNotContain("Gameplay", json); Assert.DoesNotContain("Integration Studio", json);
        Assert.True((await saves.RemoveAsync(userId, publishedJob.Id)).IsSuccess);
        Assert.True((await saves.RemoveAsync(userId, publishedJob.Id)).IsSuccess);
        Assert.Empty((await saves.ListAsync(userId)).Value.Items);
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
    private sealed class PauseSkillLock : DbCommandInterceptor
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT id FROM skills WHERE id IN", StringComparison.OrdinalIgnoreCase) && Entered.TrySetResult(true))
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private sealed class PauseEligibilityRead : DbCommandInterceptor
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("candidate_profiles", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("users", StringComparison.OrdinalIgnoreCase) && Entered.TrySetResult(true))
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    // Simulates EF wrapping a PostgreSQL deadlock/serialization error after writes have been sent.
    private sealed class InjectWrappedConflictAfterSave(string sqlState) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new DbUpdateException("Injected conflict",
                new PostgresException("Injected PostgreSQL conflict", "ERROR", "ERROR", sqlState));
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InjectedFailure();
    }

    [PostgresFact]
    public async Task Retention_PurgesAtomically_AndConcurrentRetriesAreSafe()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Retention test");
        var user = Guid.NewGuid(); var candidate = Guid.NewGuid(); var application = Guid.NewGuid();
        var now = new TestClock().UtcNow;
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "retention@example.com", Email = "retention@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidate, UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
            var job = await setup.Db.Jobs.SingleAsync(x => x.Id == draft.Id);
            job.Status = JobStatus.Expired; job.FirstPublishedAt = now.AddYears(-2); job.PublishedAt = now.AddYears(-2); job.ExpiresAt = now.AddMonths(-12); job.ApplicationsCount = 1;
            await setup.Db.SaveChangesAsync();
            setup.Db.Applications.Add(new JobApplication { Id = application, CandidateId = candidate, JobId = draft.Id,
                SubmittedAt = now.AddYears(-2), UpdatedAt = now.AddYears(-2), ConsentVersion = "v1", ConsentedAt = now.AddYears(-2) });
            setup.Db.ApplicationSnapshots.Add(new ApplicationSnapshot { ApplicationId = application, SnapshotVersion = 1,
                CandidateData = "{\"private\":\"secret\"}", JobData = "{}", Phone = "1234567890", ConsentVersion = "v1", ConsentedAt = now.AddYears(-2) });
            setup.Db.ApplicationStatusHistory.Add(new ApplicationStatusHistory { Id = Guid.NewGuid(), ApplicationId = application, NewStatus = ApplicationStatus.Submitted, ActorUserId = user, OccurredAt = now.AddYears(-2) });
            await setup.Db.SaveChangesAsync();
        }
        // Failure after database writes still rolls back the deletion, marker and audit together.
        await using (var failing = h.Session(interceptor: new FailAfterSave()))
            await Assert.ThrowsAsync<InjectedFailure>(() => new ApplicationRetentionService(failing.Db, new TestClock()).PurgeAsync(application));
        await using (var check = h.Session())
        {
            Assert.True(await check.Db.ApplicationSnapshots.AnyAsync(x => x.ApplicationId == application));
            Assert.Null((await check.Db.Applications.SingleAsync(x => x.Id == application)).SnapshotPurgedAt);
            Assert.Empty(await check.Db.AuditLogs.Where(x => x.Action == "application.snapshot_purged").ToArrayAsync());
        }
        await using var first = h.Session(); await using var second = h.Session();
        var results = await Task.WhenAll(new ApplicationRetentionService(first.Db, new TestClock()).PurgeAsync(application),
            new ApplicationRetentionService(second.Db, new TestClock()).PurgeAsync(application));
        Assert.Contains(ApplicationRetentionService.Outcome.Purged, results);
        Assert.Contains(ApplicationRetentionService.Outcome.AlreadyPurged, results);
        await using (var check = h.Session())
        {
            Assert.False(await check.Db.ApplicationSnapshots.AnyAsync(x => x.ApplicationId == application));
            Assert.Equal(now, (await check.Db.Applications.SingleAsync(x => x.Id == application)).SnapshotPurgedAt);
            Assert.Equal(1, (await check.Db.Jobs.SingleAsync(x => x.Id == draft.Id)).ApplicationsCount);
            var audit = Assert.Single(await check.Db.AuditLogs.Where(x => x.Action == "application.snapshot_purged").ToArrayAsync());
            Assert.Equal(AuditActorType.System, audit.ActorType);
            Assert.Null(audit.BeforeData); Assert.Null(audit.AfterData); Assert.Null(audit.Ip);
        }
    }

    [PostgresFact]
    public async Task Retention_RenewalAndMissingWithdrawalHistoryFailClosed()
    {
        await using var h = await DatabaseHarness.Create();
        var draft = await h.Draft("Retention renewal");
        var user = Guid.NewGuid(); var candidate = Guid.NewGuid(); var application = Guid.NewGuid();
        var now = new TestClock().UtcNow;
        await using (var setup = h.Session())
        {
            var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
            Assert.True(published.IsSuccess && published.Value.Applied);
            var city = new City { Name = "Testville", Slug = "testville", State = "Test State", StateCode = "TS" };
            setup.Db.Cities.Add(city);
            setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "renew@example.com", Email = "renew@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidate, UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
            var job = await setup.Db.Jobs.SingleAsync(x => x.Id == draft.Id);
            job.Status = JobStatus.Active; job.ExpiresAt = now.AddMonths(1);
            await setup.Db.SaveChangesAsync();
            setup.Db.Applications.Add(new JobApplication { Id = application, CandidateId = candidate, JobId = draft.Id,
                SubmittedAt = now.AddYears(-2), UpdatedAt = now.AddYears(-2), ConsentVersion = "v1", ConsentedAt = now.AddYears(-2) });
            setup.Db.ApplicationSnapshots.Add(new ApplicationSnapshot { ApplicationId = application, SnapshotVersion = 1,
                CandidateData = "{}", JobData = "{}", ConsentVersion = "v1", ConsentedAt = now.AddYears(-2) });
            setup.Db.ApplicationStatusHistory.Add(new ApplicationStatusHistory { Id = Guid.NewGuid(), ApplicationId = application, NewStatus = ApplicationStatus.Submitted, ActorUserId = user, OccurredAt = now.AddYears(-2) });
            await setup.Db.SaveChangesAsync();
        }
        await using (var session = h.Session())
            Assert.Equal(ApplicationRetentionService.Outcome.NotDue, await new ApplicationRetentionService(session.Db, new TestClock()).PurgeAsync(application));
        await using (var setup = h.Session())
        {
            var app = await setup.Db.Applications.SingleAsync(x => x.Id == application);
            app.Status = ApplicationStatus.Withdrawn; // Missing withdrawal history is never permission to delete.
            await setup.Db.SaveChangesAsync();
        }
        await using (var session = h.Session())
            Assert.Equal(ApplicationRetentionService.Outcome.Inconsistent, await new ApplicationRetentionService(session.Db, new TestClock()).PurgeAsync(application));
        await using (var setup = h.Session())
        {
            setup.Db.ApplicationStatusHistory.Add(new ApplicationStatusHistory { Id = Guid.NewGuid(), ApplicationId = application,
                PreviousStatus = ApplicationStatus.Submitted, NewStatus = ApplicationStatus.Withdrawn, ActorUserId = user, OccurredAt = now.AddMonths(-12) });
            await setup.Db.SaveChangesAsync();
        }
        await using (var session = h.Session())
            Assert.Equal(ApplicationRetentionService.Outcome.Purged, await new ApplicationRetentionService(session.Db, new TestClock()).PurgeAsync(application));
    }

    // Pause after writes, while the actual transaction and row locks are still open.
    private sealed class PauseAfterSave : SaveChangesInterceptor
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Entered.TrySetResult(true)) await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private static async Task AssertBlockedBy(Session monitor, int waitingPid, int blockingPid, Task pending)
    {
        await monitor.Db.Database.OpenConnectionAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !pending.IsCompleted)
        {
            await using var command = monitor.Db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT @blocker = ANY(pg_blocking_pids(@waiter))";
            var waiter = command.CreateParameter(); waiter.ParameterName = "waiter"; waiter.Value = waitingPid; command.Parameters.Add(waiter);
            var blocker = command.CreateParameter(); blocker.ParameterName = "blocker"; blocker.Value = blockingPid; command.Parameters.Add(blocker);
            if ((bool)(await command.ExecuteScalarAsync())!) return;
            await Task.Delay(50);
        }
        Assert.Fail("The expected PostgreSQL row-lock wait was not observed.");
    }

    private static async Task<(Guid Job, Guid Application, Guid CandidateUser)> RetentionRaceFixture(DatabaseHarness h, bool alreadyWithdrawn = false)
    {
        var draft = await h.Draft("Retention race developer");
        var user = Guid.NewGuid(); var candidate = Guid.NewGuid(); var application = Guid.NewGuid();
        var now = new TestClock().UtcNow;
        await using var setup = h.Session();
        var published = await setup.Service.TransitionAsync(new(h.Owner, h.Studio, draft.Id, JobLifecycleAction.Publish));
        Assert.True(published.IsSuccess && published.Value.Applied);
        var city = new City { Name = "Raceville", Slug = "raceville", State = "Test State", StateCode = "TS" };
        setup.Db.Cities.Add(city);
        setup.Db.Users.Add(new ApplicationUser { Id = user, AccountType = AccountType.Candidate, UserName = "race@example.com", Email = "race@example.com" });
        await setup.Db.SaveChangesAsync();
        setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = candidate, UserId = user, CityId = city.Id, FullName = "Candidate", Headline = "Developer" });
        var job = await setup.Db.Jobs.SingleAsync(x => x.Id == draft.Id);
        job.Status = JobStatus.Expired; job.FirstPublishedAt = now.AddYears(-2); job.PublishedAt = now.AddYears(-2);
        job.ExpiresAt = now.AddMonths(-12); job.ApplicationsCount = alreadyWithdrawn ? 0 : 1;
        await setup.Db.SaveChangesAsync();
        setup.Db.Applications.Add(new JobApplication { Id = application, CandidateId = candidate, JobId = draft.Id,
            Status = alreadyWithdrawn ? ApplicationStatus.Withdrawn : ApplicationStatus.Submitted,
            SubmittedAt = now.AddYears(-2), UpdatedAt = now.AddYears(-2), ConsentVersion = "v1", ConsentedAt = now.AddYears(-2) });
        setup.Db.ApplicationSnapshots.Add(new ApplicationSnapshot { ApplicationId = application, SnapshotVersion = 1,
            CandidateData = "{\"private\":\"secret\"}", JobData = "{}", Phone = "1234567890", ConsentVersion = "v1", ConsentedAt = now.AddYears(-2) });
        setup.Db.ApplicationStatusHistory.Add(new ApplicationStatusHistory { Id = Guid.NewGuid(), ApplicationId = application,
            NewStatus = ApplicationStatus.Submitted, ActorUserId = user, OccurredAt = now.AddYears(-2) });
        if (alreadyWithdrawn)
            setup.Db.ApplicationStatusHistory.Add(new ApplicationStatusHistory { Id = Guid.NewGuid(), ApplicationId = application,
                PreviousStatus = ApplicationStatus.Submitted, NewStatus = ApplicationStatus.Withdrawn, ActorUserId = user, OccurredAt = now.AddMonths(-12) });
        await setup.Db.SaveChangesAsync();
        return (draft.Id, application, user);
    }

    [PostgresFact]
    public async Task Retention_RenewalAndDeletion_LockBothOrdersAndRespectWithdrawalDeadline()
    {
        // Real lifecycle renewal holds the same Job lock as retention. The third case checks
        // that renewal does not cancel a separate, already-due withdrawal deadline.
        foreach (var scenario in new[] { (RenewFirst: true, Withdrawn: false), (RenewFirst: false, Withdrawn: false), (RenewFirst: true, Withdrawn: true) })
        {
            await using var h = await DatabaseHarness.Create();
            var fixture = await RetentionRaceFixture(h, scenario.Withdrawn);
            var pause = new PauseAfterSave();
            await using var renewal = h.Session(interceptor: scenario.RenewFirst ? pause : null);
            await using var retention = h.Session(interceptor: scenario.RenewFirst ? null : pause);
            await using var monitor = h.Session();
            await renewal.Db.Database.OpenConnectionAsync(); await retention.Db.Database.OpenConnectionAsync();
            var renewalPid = ((NpgsqlConnection)renewal.Db.Database.GetDbConnection()).ProcessID;
            var retentionPid = ((NpgsqlConnection)retention.Db.Database.GetDbConnection()).ProcessID;
            Task<Result<JobLifecycleOutcome>>? renewTask = null;
            Task<ApplicationRetentionService.Outcome>? purgeTask = null;
            try
            {
                if (scenario.RenewFirst)
                {
                    renewTask = renewal.Service.TransitionAsync(new(h.Owner, h.Studio, fixture.Job, JobLifecycleAction.Renew));
                    await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    purgeTask = new ApplicationRetentionService(retention.Db, new TestClock()).PurgeAsync(fixture.Application);
                    await AssertBlockedBy(monitor, retentionPid, renewalPid, purgeTask);
                }
                else
                {
                    purgeTask = new ApplicationRetentionService(retention.Db, new TestClock()).PurgeAsync(fixture.Application);
                    await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    renewTask = renewal.Service.TransitionAsync(new(h.Owner, h.Studio, fixture.Job, JobLifecycleAction.Renew));
                    await AssertBlockedBy(monitor, renewalPid, retentionPid, renewTask);
                }
            }
            finally { pause.Release.TrySetResult(true); }
            Assert.NotNull(renewTask); Assert.NotNull(purgeTask);
            var renewed = await renewTask!.WaitAsync(TimeSpan.FromSeconds(30));
            var outcome = await purgeTask!.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(renewed.IsSuccess && renewed.Value.Applied, renewed.IsFailure ? renewed.Error.Message : "Renewal rejected.");
            var expected = scenario.RenewFirst && !scenario.Withdrawn ? ApplicationRetentionService.Outcome.NotDue : ApplicationRetentionService.Outcome.Purged;
            Assert.Equal(expected, outcome);
            await using var check = h.Session();
            var job = await check.Db.Jobs.SingleAsync(x => x.Id == fixture.Job);
            Assert.Equal(JobStatus.Active, job.Status); Assert.True(job.ExpiresAt > new TestClock().UtcNow);
            var app = await check.Db.Applications.SingleAsync(x => x.Id == fixture.Application);
            Assert.Equal(expected == ApplicationRetentionService.Outcome.Purged, app.SnapshotPurgedAt.HasValue);
            Assert.Equal(expected != ApplicationRetentionService.Outcome.Purged, await check.Db.ApplicationSnapshots.AnyAsync(x => x.ApplicationId == fixture.Application));
            Assert.Equal(expected == ApplicationRetentionService.Outcome.Purged ? 1 : 0,
                await check.Db.AuditLogs.CountAsync(x => x.Action == "application.snapshot_purged" && x.EntityId == fixture.Application.ToString()));
        }
    }

    [PostgresFact]
    public async Task Retention_WithdrawalAndDeletion_LockBothOrders()
    {
        // Withdrawal does not exist as a service yet. This transaction is a test-only model
        // of its required Job-then-application lock order, not proof of a future implementation.
        foreach (var withdrawalFirst in new[] { true, false })
        {
            await using var h = await DatabaseHarness.Create();
            var fixture = await RetentionRaceFixture(h);
            var pause = new PauseAfterSave();
            await using var writer = h.Session();
            await using var retention = h.Session(interceptor: withdrawalFirst ? null : pause);
            await using var monitor = h.Session();
            await writer.Db.Database.OpenConnectionAsync(); await retention.Db.Database.OpenConnectionAsync();
            var writerPid = ((NpgsqlConnection)writer.Db.Database.GetDbConnection()).ProcessID;
            var retentionPid = ((NpgsqlConnection)retention.Db.Database.GetDbConnection()).ProcessID;
            var now = new TestClock().UtcNow;
            var written = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWriter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Withdraw()
            {
                await using var tx = await writer.Db.Database.BeginTransactionAsync();
                await writer.Db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM jobs WHERE id = {fixture.Job} FOR UPDATE");
                await writer.Db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM applications WHERE id = {fixture.Application} FOR UPDATE");
                await writer.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE applications SET status = 'withdrawn', updated_at = {now} WHERE id = {fixture.Application}");
                await writer.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE jobs SET applications_count = applications_count - 1 WHERE id = {fixture.Job}");
                var historyId = Guid.NewGuid();
                await writer.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO application_status_history (id, application_id, previous_status, new_status, actor_user_id, occurred_at) VALUES ({historyId}, {fixture.Application}, 'submitted', 'withdrawn', {fixture.CandidateUser}, {now})");
                written.TrySetResult(true);
                await releaseWriter.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await tx.CommitAsync();
            }
            Task? withdrawTask = null;
            Task<ApplicationRetentionService.Outcome>? purgeTask = null;
            try
            {
                if (withdrawalFirst)
                {
                    withdrawTask = Withdraw();
                    await written.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    purgeTask = new ApplicationRetentionService(retention.Db, new TestClock()).PurgeAsync(fixture.Application);
                    await AssertBlockedBy(monitor, retentionPid, writerPid, purgeTask);
                }
                else
                {
                    purgeTask = new ApplicationRetentionService(retention.Db, new TestClock()).PurgeAsync(fixture.Application);
                    await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    withdrawTask = Withdraw();
                    await AssertBlockedBy(monitor, writerPid, retentionPid, withdrawTask);
                }
            }
            finally { pause.Release.TrySetResult(true); releaseWriter.TrySetResult(true); }
            Assert.NotNull(withdrawTask); Assert.NotNull(purgeTask);
            await withdrawTask!.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(ApplicationRetentionService.Outcome.Purged, await purgeTask!.WaitAsync(TimeSpan.FromSeconds(30)));
            await using var check = h.Session();
            var app = await check.Db.Applications.SingleAsync(x => x.Id == fixture.Application);
            Assert.Equal(ApplicationStatus.Withdrawn, app.Status); Assert.NotNull(app.SnapshotPurgedAt);
            Assert.False(await check.Db.ApplicationSnapshots.AnyAsync(x => x.ApplicationId == fixture.Application));
            Assert.Equal(2, await check.Db.ApplicationStatusHistory.CountAsync(x => x.ApplicationId == fixture.Application));
            Assert.Equal(0, (await check.Db.Jobs.SingleAsync(x => x.Id == fixture.Job)).ApplicationsCount);
            Assert.Equal(1, await check.Db.AuditLogs.CountAsync(x => x.Action == "application.snapshot_purged" && x.EntityId == fixture.Application.ToString()));
        }
    }

    [PostgresFact]
    public async Task Withdrawal_EnforcesOwnershipFinalStatusRollbackAndCapacity()
    {
        await using var h = await DatabaseHarness.Create();
        var fixture = await RetentionRaceFixture(h);
        await using (var setup = h.Session())
        {
            var job = await setup.Db.Jobs.SingleAsync(x => x.Id == fixture.Job);
            job.Status = JobStatus.Full; job.ApplicationCap = 1;
            await setup.Db.SaveChangesAsync();
        }
        await using (var unauthorized = h.Session())
        {
            var denied = await new ApplicationWithdrawalService(unauthorized.Db, new TestClock()).WithdrawAsync(h.Owner, fixture.Application);
            Assert.True(denied.IsFailure); Assert.Equal("Applications.Unavailable", denied.Error.Code);
        }
        var otherUser = Guid.NewGuid();
        await using (var setup = h.Session())
        {
            var cityId = await setup.Db.CandidateProfiles.Where(x => x.UserId == fixture.CandidateUser).Select(x => x.CityId).SingleAsync();
            setup.Db.Users.Add(new ApplicationUser { Id = otherUser, AccountType = AccountType.Candidate, UserName = "other@example.com", Email = "other@example.com" });
            await setup.Db.SaveChangesAsync();
            setup.Db.CandidateProfiles.Add(new CandidateProfile { Id = Guid.NewGuid(), UserId = otherUser, CityId = cityId, FullName = "Other Candidate", Headline = "Developer" });
            await setup.Db.SaveChangesAsync();
        }
        await using (var unauthorized = h.Session())
        {
            var denied = await new ApplicationWithdrawalService(unauthorized.Db, new TestClock()).WithdrawAsync(otherUser, fixture.Application);
            Assert.True(denied.IsFailure); Assert.Equal("Applications.Unavailable", denied.Error.Code);
        }
        await using (var setup = h.Session())
        {
            var user = await setup.Db.Users.SingleAsync(x => x.Id == fixture.CandidateUser);
            user.IsActive = false; await setup.Db.SaveChangesAsync();
        }
        await using (var disabled = h.Session())
        {
            var denied = await new ApplicationWithdrawalService(disabled.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application);
            Assert.True(denied.IsFailure); Assert.Equal("Applications.Unavailable", denied.Error.Code);
        }
        await using (var setup = h.Session())
        {
            var user = await setup.Db.Users.SingleAsync(x => x.Id == fixture.CandidateUser); user.IsActive = true;
            var app = await setup.Db.Applications.SingleAsync(x => x.Id == fixture.Application); app.Status = ApplicationStatus.Hired;
            await setup.Db.SaveChangesAsync();
        }
        await using (var hired = h.Session())
        {
            var denied = await new ApplicationWithdrawalService(hired.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application);
            Assert.True(denied.IsFailure); Assert.Equal("Applications.CannotWithdraw", denied.Error.Code);
        }
        await using (var setup = h.Session())
        {
            (await setup.Db.Applications.SingleAsync(x => x.Id == fixture.Application)).Status = ApplicationStatus.Submitted;
            await setup.Db.SaveChangesAsync();
        }
        await using (var failing = h.Session(interceptor: new FailAfterSave()))
            await Assert.ThrowsAsync<InjectedFailure>(() => new ApplicationWithdrawalService(failing.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application));
        await using (var check = h.Session())
        {
            Assert.Equal(ApplicationStatus.Submitted, (await check.Db.Applications.SingleAsync(x => x.Id == fixture.Application)).Status);
            Assert.Equal(1, (await check.Db.Jobs.SingleAsync(x => x.Id == fixture.Job)).ApplicationsCount);
            Assert.Equal(1, await check.Db.ApplicationStatusHistory.CountAsync(x => x.ApplicationId == fixture.Application));
        }
        await using var first = h.Session(); await using var second = h.Session();
        var attempts = await Task.WhenAll(
            new ApplicationWithdrawalService(first.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application),
            new ApplicationWithdrawalService(second.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application));
        Assert.All(attempts, x => Assert.True(x.IsSuccess));
        Assert.Single(attempts.Where(x => x.Value.Applied)); Assert.Single(attempts.Where(x => !x.Value.Applied));
        await using (var check = h.Session())
        {
            var job = await check.Db.Jobs.SingleAsync(x => x.Id == fixture.Job);
            Assert.Equal(0, job.ApplicationsCount); Assert.Equal(JobStatus.Full, job.Status);
            Assert.Equal(ApplicationStatus.Withdrawn, (await check.Db.Applications.SingleAsync(x => x.Id == fixture.Application)).Status);
            var history = await check.Db.ApplicationStatusHistory.Where(x => x.ApplicationId == fixture.Application).OrderBy(x => x.OccurredAt).ToArrayAsync();
            Assert.Equal(2, history.Length); Assert.Equal(ApplicationStatus.Submitted, history[1].PreviousStatus);
            Assert.Equal(ApplicationStatus.Withdrawn, history[1].NewStatus); Assert.Equal(fixture.CandidateUser, history[1].ActorUserId);
            Assert.True(await check.Db.ApplicationSnapshots.AnyAsync(x => x.ApplicationId == fixture.Application));
        }
    }

    [PostgresFact]
    public async Task Withdrawal_InconsistentCapacityOrHistoryFailsClosed()
    {
        await using var h = await DatabaseHarness.Create();
        var fixture = await RetentionRaceFixture(h);
        await using (var setup = h.Session())
        {
            (await setup.Db.Jobs.SingleAsync(x => x.Id == fixture.Job)).ApplicationsCount = 0;
            await setup.Db.SaveChangesAsync();
        }
        await using (var session = h.Session())
        {
            var result = await new ApplicationWithdrawalService(session.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application);
            Assert.True(result.IsFailure); Assert.Equal("Applications.Inconsistent", result.Error.Code);
        }
        await using (var setup = h.Session())
        {
            (await setup.Db.Jobs.SingleAsync(x => x.Id == fixture.Job)).ApplicationsCount = 1;
            var history = await setup.Db.ApplicationStatusHistory.SingleAsync(x => x.ApplicationId == fixture.Application);
            history.NewStatus = ApplicationStatus.Rejected;
            await setup.Db.SaveChangesAsync();
        }
        await using (var session = h.Session())
        {
            var result = await new ApplicationWithdrawalService(session.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application);
            Assert.True(result.IsFailure); Assert.Equal("Applications.Inconsistent", result.Error.Code);
            Assert.Equal(ApplicationStatus.Submitted, (await session.Db.Applications.SingleAsync(x => x.Id == fixture.Application)).Status);
            Assert.Equal(1, (await session.Db.Jobs.SingleAsync(x => x.Id == fixture.Job)).ApplicationsCount);
        }
    }

    [PostgresFact]
    public async Task Withdrawal_ActualServiceAndRetention_LockBothOrders()
    {
        foreach (var withdrawalFirst in new[] { true, false })
        {
            await using var h = await DatabaseHarness.Create();
            var fixture = await RetentionRaceFixture(h);
            var pause = new PauseAfterSave();
            await using var withdrawal = h.Session(interceptor: withdrawalFirst ? pause : null);
            await using var retention = h.Session(interceptor: withdrawalFirst ? null : pause);
            await using var monitor = h.Session();
            await withdrawal.Db.Database.OpenConnectionAsync(); await retention.Db.Database.OpenConnectionAsync();
            var withdrawalPid = ((NpgsqlConnection)withdrawal.Db.Database.GetDbConnection()).ProcessID;
            var retentionPid = ((NpgsqlConnection)retention.Db.Database.GetDbConnection()).ProcessID;
            Task<Result<HireGameDev.Application.Features.Applications.ApplicationWithdrawalResult>>? withdrawTask = null;
            Task<ApplicationRetentionService.Outcome>? purgeTask = null;
            try
            {
                if (withdrawalFirst)
                {
                    withdrawTask = new ApplicationWithdrawalService(withdrawal.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application);
                    await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    purgeTask = new ApplicationRetentionService(retention.Db, new TestClock()).PurgeAsync(fixture.Application);
                    await AssertBlockedBy(monitor, retentionPid, withdrawalPid, purgeTask);
                }
                else
                {
                    purgeTask = new ApplicationRetentionService(retention.Db, new TestClock()).PurgeAsync(fixture.Application);
                    await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    withdrawTask = new ApplicationWithdrawalService(withdrawal.Db, new TestClock()).WithdrawAsync(fixture.CandidateUser, fixture.Application);
                    await AssertBlockedBy(monitor, withdrawalPid, retentionPid, withdrawTask);
                }
            }
            finally { pause.Release.TrySetResult(true); }
            Assert.NotNull(withdrawTask); Assert.NotNull(purgeTask);
            var withdrawn = await withdrawTask!.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(withdrawn.IsSuccess && withdrawn.Value.Applied, withdrawn.IsFailure ? withdrawn.Error.Message : "Withdrawal was not applied.");
            Assert.Equal(ApplicationRetentionService.Outcome.Purged, await purgeTask!.WaitAsync(TimeSpan.FromSeconds(30)));
            await using var check = h.Session();
            var app = await check.Db.Applications.SingleAsync(x => x.Id == fixture.Application);
            Assert.Equal(ApplicationStatus.Withdrawn, app.Status); Assert.NotNull(app.SnapshotPurgedAt);
            Assert.False(await check.Db.ApplicationSnapshots.AnyAsync(x => x.ApplicationId == fixture.Application));
            Assert.Equal(2, await check.Db.ApplicationStatusHistory.CountAsync(x => x.ApplicationId == fixture.Application));
            Assert.Equal(0, (await check.Db.Jobs.SingleAsync(x => x.Id == fixture.Job)).ApplicationsCount);
            Assert.Equal(1, await check.Db.AuditLogs.CountAsync(x => x.Action == "application.snapshot_purged" && x.EntityId == fixture.Application.ToString()));
        }
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
