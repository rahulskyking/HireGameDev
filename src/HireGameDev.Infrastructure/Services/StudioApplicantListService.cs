using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Prototype only. A single SQL statement gives authorization and data the same database
// snapshot. It does not revoke a response already in flight when access changes afterwards.
internal sealed class StudioApplicantListService(AppDbContext db) : IStudioApplicantListService
{
    public async Task<Result<IReadOnlyList<StudioApplicantListItem>>> ListAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql())
            return Result.Failure<IReadOnlyList<StudioApplicantListItem>>(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure<IReadOnlyList<StudioApplicantListItem>>(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        if (actorUserId == Guid.Empty || jobId == Guid.Empty)
            return Result.Success<IReadOnlyList<StudioApplicantListItem>>(Array.Empty<StudioApplicantListItem>());

        // No live Candidate data is returned. Snapshot existence and purged/withdrawn state
        // are checked in the same query as membership, account state and blocks.
        var items = await (from app in db.Applications.AsNoTracking()
            join job in db.Jobs.AsNoTracking() on app.JobId equals job.Id
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            join member in db.StudioMembers.AsNoTracking() on studio.Id equals member.StudioId
            join actor in db.Users.AsNoTracking() on member.UserId equals actor.Id
            join candidate in db.CandidateProfiles.AsNoTracking() on app.CandidateId equals candidate.Id
            join candidateUser in db.Users.AsNoTracking() on candidate.UserId equals candidateUser.Id
            where job.Id == jobId && actor.Id == actorUserId && actor.IsActive && actor.DeletedAt == null && actor.AccountType == AccountType.Studio
                && studio.DeletedAt == null && studio.SuspendedAt == null && studio.VerificationStatus == StudioVerificationStatus.Verified
                && (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer)
                && candidate.DeletedAt == null && candidateUser.IsActive && candidateUser.DeletedAt == null && candidateUser.AccountType == AccountType.Candidate
                && app.Status != ApplicationStatus.Withdrawn && app.SnapshotPurgedAt == null
                && db.ApplicationSnapshots.Any(s => s.ApplicationId == app.Id)
                && !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Id && b.StudioId == studio.Id)
            orderby app.SubmittedAt descending, app.Id descending
            select new StudioApplicantListItem(app.Id, app.Status, app.SubmittedAt)).Take(100).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<StudioApplicantListItem>>(items);
    }
}
