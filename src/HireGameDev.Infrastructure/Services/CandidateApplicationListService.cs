using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Only a Candidate's own application metadata; no Studio-private data or snapshot contents.
internal sealed class CandidateApplicationListService(AppDbContext db) : ICandidateApplicationListService
{
    public async Task<Result<IReadOnlyList<CandidateApplicationListItem>>> ListAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return Result.Failure<IReadOnlyList<CandidateApplicationListItem>>(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) return Result.Failure<IReadOnlyList<CandidateApplicationListItem>>(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        var items = await (from app in db.Applications.AsNoTracking()
            join candidate in db.CandidateProfiles.AsNoTracking() on app.CandidateId equals candidate.Id
            join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
            join job in db.Jobs.AsNoTracking() on app.JobId equals job.Id
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            where actorUserId != Guid.Empty && user.Id == actorUserId && user.IsActive && user.DeletedAt == null &&
                user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
            orderby app.SubmittedAt descending, app.Id descending
            select new CandidateApplicationListItem(app.Id, job.Title ?? "Untitled job", studio.Name, app.Status, app.SubmittedAt))
            .Take(100).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<CandidateApplicationListItem>>(items);
    }
}
