using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

/// <summary>Read-only, advisory eligibility; submission must independently revalidate within its transaction.</summary>
internal sealed class ApplicationEligibilityService(AppDbContext db, IClock clock) : IApplicationEligibilityService
{
    public async Task<Result<ApplicationEligibility>> CheckAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var candidateId = await (from candidate in db.CandidateProfiles.AsNoTracking()
            join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
            where actorUserId != Guid.Empty && user.Id == actorUserId && user.IsActive && user.DeletedAt == null &&
                user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
            select (Guid?)candidate.Id).SingleOrDefaultAsync(cancellationToken);
        if (!candidateId.HasValue)
            return Result.Failure<ApplicationEligibility>(Error.Forbidden("Applications.CandidateRequired", "An active Candidate account is required."));

        var now = clock.UtcNow;
        // Blocked, non-public and unknown jobs all produce the same response. Only minimal
        // capacity data is selected; no Studio or job details are returned by this service.
        var job = await (from j in db.Jobs.AsNoTracking()
            join studio in db.StudioProfiles.AsNoTracking() on j.StudioId equals studio.Id
            where j.Id == jobId && j.Status == JobStatus.Active && j.ExpiresAt != null && j.ExpiresAt > now &&
                studio.VerificationStatus == StudioVerificationStatus.Verified && studio.SuspendedAt == null && studio.DeletedAt == null &&
                !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidateId.Value && b.StudioId == studio.Id)
            select new { j.ApplicationCap, j.ApplicationsCount }).SingleOrDefaultAsync(cancellationToken);
        if (job is null)
            return Result.Failure<ApplicationEligibility>(Error.NotFound("Applications.Unavailable", "This listing is unavailable."));

        if (await db.Applications.AsNoTracking().AnyAsync(a => a.CandidateId == candidateId.Value && a.JobId == jobId, cancellationToken))
            return Result.Success(ApplicationEligibility.AlreadyApplied);
        return Result.Success(job.ApplicationsCount >= job.ApplicationCap
            ? ApplicationEligibility.CapacityReached : ApplicationEligibility.Eligible);
    }
}
