using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class ApplicationEligibilityServiceTests
{
    [Fact]
    public async Task Eligibility_IsAdvisory_AndWithdrawnApplicationStillPreventsReapply()
    {
        using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser { Id = Guid.NewGuid(), AccountType = AccountType.Candidate, IsActive = true };
        var candidate = new CandidateProfile { Id = Guid.NewGuid(), UserId = user.Id };
        var studio = new StudioProfile { Id = Guid.NewGuid(), VerificationStatus = StudioVerificationStatus.Verified };
        var job = new Job { Id = Guid.NewGuid(), StudioId = studio.Id, Status = JobStatus.Active, ExpiresAt = now.AddDays(1), ApplicationCap = 2, ApplicationsCount = 0 };
        db.Users.Add(user); db.CandidateProfiles.Add(candidate); db.StudioProfiles.Add(studio); db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var service = new ApplicationEligibilityService(db, new SystemClock());
        Assert.Equal(ApplicationEligibility.Eligible, (await service.CheckAsync(user.Id, job.Id)).Value);
        job.ApplicationsCount = 2; await db.SaveChangesAsync();
        Assert.Equal(ApplicationEligibility.CapacityReached, (await service.CheckAsync(user.Id, job.Id)).Value);
        db.Applications.Add(new JobApplication { Id = Guid.NewGuid(), JobId = job.Id, CandidateId = candidate.Id, Status = ApplicationStatus.Withdrawn });
        await db.SaveChangesAsync();
        Assert.Equal(ApplicationEligibility.AlreadyApplied, (await service.CheckAsync(user.Id, job.Id)).Value);
    }

    [Fact]
    public async Task RestrictedCandidateAndHiddenJobs_AreDeniedWithoutLeakingJobDetails()
    {
        using var db = CreateDb();
        var user = new ApplicationUser { Id = Guid.NewGuid(), AccountType = AccountType.Candidate, IsActive = true };
        var candidate = new CandidateProfile { Id = Guid.NewGuid(), UserId = user.Id };
        var studio = new StudioProfile { Id = Guid.NewGuid(), VerificationStatus = StudioVerificationStatus.Verified };
        var job = new Job { Id = Guid.NewGuid(), StudioId = studio.Id, Status = JobStatus.Active, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        db.Users.Add(user); db.CandidateProfiles.Add(candidate); db.StudioProfiles.Add(studio); db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var service = new ApplicationEligibilityService(db, new SystemClock());
        var missing = await service.CheckAsync(user.Id, Guid.NewGuid());
        db.CandidateStudioBlocks.Add(new CandidateStudioBlock { CandidateId = candidate.Id, StudioId = studio.Id });
        await db.SaveChangesAsync();
        Assert.Equal(missing.Error, (await service.CheckAsync(user.Id, job.Id)).Error);
        db.CandidateStudioBlocks.RemoveRange(db.CandidateStudioBlocks); studio.SuspendedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(missing.Error, (await service.CheckAsync(user.Id, job.Id)).Error);
        studio.SuspendedAt = null; job.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.Equal(missing.Error, (await service.CheckAsync(user.Id, job.Id)).Error);
        user.AccountType = AccountType.Studio; await db.SaveChangesAsync();
        Assert.Equal("Applications.CandidateRequired", (await service.CheckAsync(user.Id, job.Id)).Error.Code);
        user.AccountType = AccountType.Candidate; user.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal("Applications.CandidateRequired", (await service.CheckAsync(user.Id, job.Id)).Error.Code);
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new SystemClock());
}
