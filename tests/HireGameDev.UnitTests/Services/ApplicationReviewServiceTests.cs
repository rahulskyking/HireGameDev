using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class ApplicationReviewServiceTests
{
    [Fact]
    public async Task Review_OnlyReturnsTheCandidatesAllowlistedSnapshot_AndDoesNotSubmit()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new SystemClock());
        var user = new ApplicationUser { Id = Guid.NewGuid(), AccountType = AccountType.Candidate, IsActive = true };
        var city = new City { Id = 1, Name = "Kolkata" };
        var candidate = new CandidateProfile { Id = Guid.NewGuid(), UserId = user.Id, CityId = city.Id, FullName = "Alex", Headline = "Developer", CurrentCtcLpa = 8, ShowCurrentCtc = false };
        var studio = new StudioProfile { Id = Guid.NewGuid(), Name = "Studio", VerificationStatus = StudioVerificationStatus.Verified };
        var job = new Job { Id = Guid.NewGuid(), StudioId = studio.Id, Title = "Developer", Status = JobStatus.Active, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), ScreeningQuestion = "Why?" };
        db.Users.Add(user); db.Cities.Add(city); db.CandidateProfiles.Add(candidate); db.StudioProfiles.Add(studio); db.Jobs.Add(job);
        db.ShippedTitles.Add(new ShippedTitle { Id = Guid.NewGuid(), CandidateId = candidate.Id, GameName = "Private", StudioName = "Hidden", Role = "Engineer", IsNda = true, HideStudioInPublic = true });
        await db.SaveChangesAsync();
        var service = new ApplicationReviewService(db, new ApplicationEligibilityService(db, new SystemClock()));
        var result = await service.ReviewAsync(user.Id, job.Id, "For the team");
        Assert.True(result.IsSuccess);
        Assert.Equal("Alex", result.Value.Candidate.FullName);
        Assert.Null(result.Value.Candidate.CurrentCtcLpa);
        Assert.Equal("Confidential Project (Under NDA)", Assert.Single(result.Value.Candidate.ShippedGames).GameName);
        Assert.Equal("For the team", result.Value.Job.ScreeningAnswer);
        Assert.Empty(db.Applications);
        Assert.True((await service.ReviewAsync(user.Id, job.Id, new string('x', 2001))).IsFailure);
        db.CandidateStudioBlocks.Add(new CandidateStudioBlock { CandidateId = candidate.Id, StudioId = studio.Id });
        await db.SaveChangesAsync();
        Assert.Equal("Applications.Unavailable", (await service.ReviewAsync(user.Id, job.Id, null)).Error.Code);
    }
}
