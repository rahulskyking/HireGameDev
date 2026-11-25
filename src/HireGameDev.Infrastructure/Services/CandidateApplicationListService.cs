using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Only a Candidate's own application metadata; no Studio-private data or snapshot contents.
internal sealed class CandidateApplicationListService(AppDbContext db) : ICandidateApplicationListService
{
    public async Task<Result<CandidateApplicationPage>> ListAsync(Guid actorUserId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return Result.Failure<CandidateApplicationPage>(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) return Result.Failure<CandidateApplicationPage>(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue - 100)
            return Result.Failure<CandidateApplicationPage>(Error.Validation("Applications.InvalidPage", "Choose a valid page and page size."));
        var items = await (from app in db.Applications.AsNoTracking()
            join candidate in db.CandidateProfiles.AsNoTracking() on app.CandidateId equals candidate.Id
            join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
            join job in db.Jobs.AsNoTracking() on app.JobId equals job.Id
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            where actorUserId != Guid.Empty && user.Id == actorUserId && user.IsActive && user.DeletedAt == null &&
                user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
            orderby app.SubmittedAt descending, app.Id descending
            select new CandidateApplicationListItem(app.Id, job.Title ?? "Untitled job", studio.Name, app.Status, app.SubmittedAt))
            .Skip((page - 1) * pageSize).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        return Result.Success(new CandidateApplicationPage(items, page, hasMore));
    }
}
