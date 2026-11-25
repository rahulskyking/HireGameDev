using System.Text.Json;
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
    public async Task<Result<StudioApplicantPage>> ListAsync(Guid actorUserId, Guid jobId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default, ApplicationStatus? status = null)
    {
        if (!db.Database.IsNpgsql())
            return Result.Failure<StudioApplicantPage>(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure<StudioApplicantPage>(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        // Bound offset and page size before arithmetic or querying.
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue - 100)
            return Result.Failure<StudioApplicantPage>(Error.Validation("Applications.InvalidPage", "Choose a valid page and page size."));
        if (status.HasValue && !Enum.IsDefined(status.Value))
            return Result.Failure<StudioApplicantPage>(Error.Validation("Applications.InvalidStatus", "Choose a valid status."));
        if (actorUserId == Guid.Empty || jobId == Guid.Empty)
            return Result.Success(new StudioApplicantPage(Array.Empty<StudioApplicantListItem>(), false));

        // No live Candidate data is returned. Snapshot existence and purged/withdrawn state
        // are checked in the same query as membership, account state and blocks.
        var items = await (from app in db.Applications.AsNoTracking()
            join job in db.Jobs.AsNoTracking() on app.JobId equals job.Id
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            join member in db.StudioMembers.AsNoTracking() on studio.Id equals member.StudioId
            join actor in db.Users.AsNoTracking() on member.UserId equals actor.Id
            join candidate in db.CandidateProfiles.AsNoTracking() on app.CandidateId equals candidate.Id
            join candidateUser in db.Users.AsNoTracking() on candidate.UserId equals candidateUser.Id
            join snapshot in db.ApplicationSnapshots.AsNoTracking() on app.Id equals snapshot.ApplicationId
            where job.Id == jobId && actor.Id == actorUserId && actor.IsActive && actor.DeletedAt == null && actor.AccountType == AccountType.Studio
                && studio.DeletedAt == null && studio.SuspendedAt == null && studio.VerificationStatus == StudioVerificationStatus.Verified
                && (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer)
                && candidate.DeletedAt == null && candidateUser.IsActive && candidateUser.DeletedAt == null && candidateUser.AccountType == AccountType.Candidate
                && app.Status != ApplicationStatus.Withdrawn && app.SnapshotPurgedAt == null
                && (!status.HasValue || app.Status == status)
                && !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Id && b.StudioId == studio.Id)
            orderby app.SubmittedAt descending, app.Id descending
            select new { app.Id, app.Status, app.SubmittedAt, snapshot.SnapshotVersion, snapshot.CandidateData })
            .Skip((page - 1) * pageSize).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        var result = items.Select(x => new StudioApplicantListItem(x.Id, x.Status, x.SubmittedAt,
            SnapshotName(x.SnapshotVersion, x.CandidateData))).ToArray();
        return Result.Success(new StudioApplicantPage(result, hasMore));
    }

    // Only expose the versioned snapshot's name, never an unknown JSON field or live profile.
    // Invalid snapshots remain generic rather than falling back to a different source.
    private static string? SnapshotName(int version, string json)
    {
        if (version != 1) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("FullName", out var name) || name.ValueKind != JsonValueKind.String) return null;
            var value = name.GetString();
            return string.IsNullOrWhiteSpace(value) || value.Length > 200 ? null : value;
        }
        catch (JsonException) { return null; }
    }
}
