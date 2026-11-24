using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Entities;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class ApplicationSubmissionTokenTests
{
    [Fact]
    public async Task TokenRequiresConsentCorrectActorAndJobAndExpires_WithoutInMemorySubmissionFallback()
    {
        var clock = new TestClock();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, clock);
        var actor = Guid.NewGuid();
        var studio = new StudioProfile { Id = Guid.NewGuid(), Name = "Studio" };
        var job = new Job { Id = Guid.NewGuid(), StudioId = studio.Id, Title = "Game Developer" };
        var candidate = new CandidateProfile { FullName = "Alex", City = new City { Name = "Kolkata" } };
        var draft = ApplicationSnapshotBuilder.Create(candidate, job, studio, null);
        var service = new ApplicationSubmissionService(db, new StubReview(draft), clock, new EphemeralDataProtectionProvider());
        var prepared = await service.PrepareAsync(actor, job.Id, null);
        Assert.True(prepared.IsSuccess);
        var token = prepared.Value.ReviewToken;
        Assert.Equal("Applications.ConsentRequired", (await service.SubmitAsync(new(actor, job.Id, null, token, false))).Error.Code);
        Assert.Equal("Applications.ReviewStale", (await service.SubmitAsync(new(Guid.NewGuid(), job.Id, null, token, true))).Error.Code);
        Assert.Equal("Applications.ReviewStale", (await service.SubmitAsync(new(actor, Guid.NewGuid(), null, token, true))).Error.Code);
        Assert.Equal("Applications.ReviewStale", (await service.SubmitAsync(new(actor, job.Id, null, "invalid-token", true))).Error.Code);
        Assert.Equal("Applications.PostgresRequired", (await service.SubmitAsync(new(actor, job.Id, null, token, true))).Error.Code);
        clock.UtcNow = prepared.Value.ExpiresAt;
        Assert.Equal("Applications.ReviewStale", (await service.SubmitAsync(new(actor, job.Id, null, token, true))).Error.Code);
        Assert.Empty(db.Applications);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class StubReview(ApplicationSnapshotDraft draft) : IApplicationReviewService
    {
        public Task<Result<ApplicationSnapshotDraft>> ReviewAsync(Guid actorUserId, Guid jobId, string? screeningAnswer, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(draft));
    }
}
