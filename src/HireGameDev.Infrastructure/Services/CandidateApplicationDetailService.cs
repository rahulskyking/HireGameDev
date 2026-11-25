using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Authorized owner and timeline are read in one statement; an in-flight response cannot
// be recalled after account state changes. This does not grant Studio or snapshot access.
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
            where app.Id == applicationId && user.Id == actorUserId && user.IsActive && user.DeletedAt == null &&
                user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
            orderby history.OccurredAt descending, history.Id descending
            select new { app.Id, app.Status, app.SubmittedAt, JobTitle = job.Title ?? "Untitled job", StudioName = studio.Name,
                history.PreviousStatus, history.NewStatus, history.OccurredAt })
            .Take(MaxEvents + 1).ToListAsync(cancellationToken);
        // Missing history is an integrity problem, not permission to expose other data.
        if (rows.Count == 0) return Unavailable();
        var hasEarlier = rows.Count > MaxEvents;
        if (hasEarlier) rows.RemoveAt(rows.Count - 1);
        var head = rows[0];
        var events = rows.Select(x => new CandidateApplicationEvent(x.PreviousStatus, x.NewStatus, x.OccurredAt)).ToArray();
        return Result.Success(new CandidateApplicationDetail(head.Id, head.JobTitle, head.StudioName, head.Status, head.SubmittedAt, events, hasEarlier));
    }

    private static Result<CandidateApplicationDetail> Unavailable() =>
        Result.Failure<CandidateApplicationDetail>(Error.NotFound("Applications.Unavailable", "This application is unavailable."));
}
