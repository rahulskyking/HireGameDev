using System.Text.Json;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Prototype only. The authorization and snapshot are read in one PostgreSQL statement;
// access changes after that statement cannot retract a response already in flight.
internal sealed class StudioApplicantDetailService(AppDbContext db) : IStudioApplicantDetailService
{
    public async Task<Result<StudioApplicantDetail>> GetAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql())
            return Result.Failure<StudioApplicantDetail>(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure<StudioApplicantDetail>(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        if (actorUserId == Guid.Empty || applicationId == Guid.Empty) return Unavailable();

        var row = await (from app in db.Applications.AsNoTracking()
            join job in db.Jobs.AsNoTracking() on app.JobId equals job.Id
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            join member in db.StudioMembers.AsNoTracking() on studio.Id equals member.StudioId
            join actor in db.Users.AsNoTracking() on member.UserId equals actor.Id
            join candidate in db.CandidateProfiles.AsNoTracking() on app.CandidateId equals candidate.Id
            join candidateUser in db.Users.AsNoTracking() on candidate.UserId equals candidateUser.Id
            join snapshot in db.ApplicationSnapshots.AsNoTracking() on app.Id equals snapshot.ApplicationId
            where app.Id == applicationId && actor.Id == actorUserId && actor.IsActive && actor.DeletedAt == null && actor.AccountType == AccountType.Studio
                && studio.DeletedAt == null && studio.SuspendedAt == null && studio.VerificationStatus == StudioVerificationStatus.Verified
                && (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer)
                && candidate.DeletedAt == null && candidateUser.IsActive && candidateUser.DeletedAt == null && candidateUser.AccountType == AccountType.Candidate
                && app.Status != ApplicationStatus.Withdrawn && app.SnapshotPurgedAt == null
                && !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Id && b.StudioId == studio.Id)
            select new { app.Id, app.Status, app.SubmittedAt, snapshot.SnapshotVersion, snapshot.CandidateData, snapshot.JobData,
                CanChangeStatus = member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter,
                Phone = app.Status == ApplicationStatus.Shortlisted || app.Status == ApplicationStatus.Hired ? snapshot.Phone : null })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.SnapshotVersion != 1) return Unavailable();
        try
        {
            // Typed deserialization prevents unrecognized stored JSON properties from being returned.
            // Fail closed on incomplete snapshots rather than falling back to live profile data.
            var candidate = JsonSerializer.Deserialize<CandidateApplicationSnapshot>(row.CandidateData);
            var job = JsonSerializer.Deserialize<JobApplicationSnapshot>(row.JobData);
            if (candidate is null || job is null || string.IsNullOrWhiteSpace(candidate.FullName) ||
                string.IsNullOrWhiteSpace(candidate.Headline) || string.IsNullOrWhiteSpace(candidate.City) ||
                candidate.Skills is null || candidate.Experience is null || candidate.Education is null ||
                candidate.Portfolio is null || candidate.ShippedGames is null ||
                string.IsNullOrWhiteSpace(job.JobTitle) || string.IsNullOrWhiteSpace(job.StudioName)) return Unavailable();
            return Result.Success(new StudioApplicantDetail(row.Id, row.Status, row.SubmittedAt, candidate, job, row.Phone, row.CanChangeStatus));
        }
        catch (JsonException) { return Unavailable(); }
        catch (NotSupportedException) { return Unavailable(); }
    }

    private static Result<StudioApplicantDetail> Unavailable() =>
        Result.Failure<StudioApplicantDetail>(Error.NotFound("Applications.Unavailable", "This application is unavailable."));
}
