using System.Text.Json;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Authorized owner and timeline are read in one statement; an in-flight response cannot
// be recalled after account state changes. Snapshot access is limited to its authenticated owner.
internal sealed class CandidateApplicationDetailService(AppDbContext db) : ICandidateApplicationDetailService
{
    private const int MaxEvents = 50;

    public async Task<Result<CandidateApplicationDetail>> GetAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return Result.Failure<CandidateApplicationDetail>(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure<CandidateApplicationDetail>(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        if (actorUserId == Guid.Empty || applicationId == Guid.Empty) return Unavailable();
        var rows = await (from app in db.Applications.AsNoTracking()
            join candidate in db.CandidateProfiles.AsNoTracking() on app.CandidateId equals candidate.Id
            join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
            join job in db.Jobs.AsNoTracking() on app.JobId equals job.Id
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            join history in db.ApplicationStatusHistory.AsNoTracking() on app.Id equals history.ApplicationId
            join snapshot in db.ApplicationSnapshots.AsNoTracking() on app.Id equals snapshot.ApplicationId into snapshots
            from snapshot in snapshots.DefaultIfEmpty()
            where app.Id == applicationId && user.Id == actorUserId && user.IsActive && user.DeletedAt == null &&
                user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
            orderby history.OccurredAt descending, history.Id descending
            select new { app.Id, app.Status, app.SubmittedAt, JobTitle = job.Title ?? "Untitled job", StudioName = studio.Name,
                history.PreviousStatus, history.NewStatus, history.OccurredAt, app.SnapshotPurgedAt, SnapshotExists = snapshot != null,
                CandidateData = app.SnapshotPurgedAt == null && snapshot != null && snapshot.SnapshotVersion == 1 ? snapshot.CandidateData : null,
                JobData = app.SnapshotPurgedAt == null && snapshot != null && snapshot.SnapshotVersion == 1 ? snapshot.JobData : null,
                Phone = app.SnapshotPurgedAt == null && snapshot != null && snapshot.SnapshotVersion == 1 ? snapshot.Phone : null })
            .Take(MaxEvents + 1).ToListAsync(cancellationToken);
        // Missing history is an integrity problem, not permission to expose other data.
        if (rows.Count == 0) return Unavailable();
        var hasEarlier = rows.Count > MaxEvents;
        if (hasEarlier) rows.RemoveAt(rows.Count - 1);
        var head = rows[0];
        var events = rows.Select(x => new CandidateApplicationEvent(x.PreviousStatus, x.NewStatus, x.OccurredAt)).ToArray();
        ApplicationSkillMatch? match = null;
        ApplicationFitScore? fit = null;
        var usesCurrentJobRequirements = false;
        ApplicationSnapshotDraft? submittedSnapshot = null;
        if (head.CandidateData is not null && head.JobData is not null)
        {
            try {
                var candidateSnapshot = JsonSerializer.Deserialize<CandidateApplicationSnapshot>(head.CandidateData);
                var jobSnapshot = JsonSerializer.Deserialize<JobApplicationSnapshot>(head.JobData);
                if (ValidSnapshot(candidateSnapshot, jobSnapshot))
                {
                    submittedSnapshot = new ApplicationSnapshotDraft(1, candidateSnapshot!, jobSnapshot!, head.Phone);
                    // Withdrawal revokes Studio access and removes matching; the owner can still
                    // inspect their stored data until it is purged.
                    if (head.Status != ApplicationStatus.Withdrawn)
                    {
                        match = ApplicationSkillMatch.Create(candidateSnapshot, jobSnapshot);
                        fit = ApplicationFitScore.Create(candidateSnapshot, jobSnapshot);
                        if (jobSnapshot!.MatchInputs is null)
                        {
                            fit = await LegacyApplicationMatch.GetAsync(db, actorUserId, applicationId, candidateSnapshot!, jobSnapshot, cancellationToken);
                            usesCurrentJobRequirements = fit is not null;
                        }
                    }
                }
            }
            catch (JsonException) { /* Matching is optional; timeline remains available. */ }
            catch (NotSupportedException) { }
        }
        return Result.Success(new CandidateApplicationDetail(head.Id, head.JobTitle, head.StudioName, head.Status, head.SubmittedAt, events, hasEarlier, match, fit, usesCurrentJobRequirements, submittedSnapshot, head.SnapshotPurgedAt.HasValue && !head.SnapshotExists));
    }

    // Fail closed for malformed or unsupported snapshots; never substitute the live profile.
    private static bool ValidSnapshot(CandidateApplicationSnapshot? candidate, JobApplicationSnapshot? job) =>
        candidate is not null && job is not null && !string.IsNullOrWhiteSpace(candidate.FullName) &&
        !string.IsNullOrWhiteSpace(candidate.Headline) && !string.IsNullOrWhiteSpace(candidate.City) &&
        candidate.Skills is not null && candidate.Experience is not null && candidate.Education is not null &&
        candidate.Portfolio is not null && candidate.ShippedGames is not null &&
        candidate.Skills.All(x => x is not null && !string.IsNullOrWhiteSpace(x.Name)) &&
        candidate.Experience.All(x => x is not null && !string.IsNullOrWhiteSpace(x.Company) && !string.IsNullOrWhiteSpace(x.Title)) &&
        candidate.Education.All(x => x is not null && !string.IsNullOrWhiteSpace(x.Institution) && !string.IsNullOrWhiteSpace(x.Degree)) &&
        candidate.Portfolio.All(x => x is not null && x.Skills is not null && x.Skills.All(s => !string.IsNullOrWhiteSpace(s)) && !string.IsNullOrWhiteSpace(x.Url)) &&
        candidate.ShippedGames.All(x => x is not null && x.Platforms is not null && x.Platforms.All(s => !string.IsNullOrWhiteSpace(s)) &&
            !string.IsNullOrWhiteSpace(x.GameName) && !string.IsNullOrWhiteSpace(x.StudioName) && !string.IsNullOrWhiteSpace(x.Role)) &&
        !string.IsNullOrWhiteSpace(job.JobTitle) && !string.IsNullOrWhiteSpace(job.StudioName);

    private static Result<CandidateApplicationDetail> Unavailable() =>
        Result.Failure<CandidateApplicationDetail>(Error.NotFound("Applications.Unavailable", "This application is unavailable."));
}
