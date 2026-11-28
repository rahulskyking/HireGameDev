using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HireGameDev.Infrastructure.Services;

internal sealed class SavedJobService(AppDbContext db, IPublicJobQueryService publicJobs, IClock clock, IPlanEntitlementService? plans = null) : ISavedJobService, ISavedJobStateQuery
{
    private static Error Denied() => Error.Forbidden("SavedJobs.Forbidden", "A Candidate account is required.");
    private static Error Unavailable() => Error.NotFound("SavedJobs.Unavailable", "This listing is unavailable.");

    private Task<Guid?> CandidateAsync(Guid actor, CancellationToken ct) =>
        (from candidate in db.CandidateProfiles.AsNoTracking()
         join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
         where actor != Guid.Empty && user.Id == actor && user.IsActive && user.DeletedAt == null &&
             user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
         select (Guid?)candidate.Id).SingleOrDefaultAsync(ct);

    public async Task<Result<IReadOnlySet<Guid>>> GetSavedIdsAsync(Guid actorUserId, IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        if (jobIds.Count > 50) return Result.Failure<IReadOnlySet<Guid>>(Error.Validation("SavedJobs.TooManyJobs", "Choose up to 50 jobs."));
        var candidate = await CandidateAsync(actorUserId, cancellationToken);
        if (!candidate.HasValue) return Result.Failure<IReadOnlySet<Guid>>(Denied());
        var ids = jobIds.Distinct().ToArray();
        var saved = await db.CandidateSavedJobs.AsNoTracking()
            .Where(s => s.CandidateId == candidate.Value && ids.Contains(s.JobId))
            .Select(s => s.JobId).ToArrayAsync(cancellationToken);
        return Result.Success<IReadOnlySet<Guid>>(saved.ToHashSet());
    }

    public async Task<Result<bool>> IsSavedAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var candidate = await CandidateAsync(actorUserId, cancellationToken);
        if (!candidate.HasValue) return Result.Failure<bool>(Denied());
        var slug = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.Slug).SingleOrDefaultAsync(cancellationToken);
        if (slug is null || (await publicJobs.GetBySlugAsync(slug, cancellationToken, actorUserId)).IsFailure)
            return Result.Failure<bool>(Unavailable());
        return Result.Success(await db.CandidateSavedJobs.AnyAsync(s => s.CandidateId == candidate.Value && s.JobId == jobId, cancellationToken));
    }

    public async Task<Result> SaveAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (plans is null) return await SaveCoreAsync(actorUserId, jobId, cancellationToken);
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure(Error.Conflict("SavedJobs.TransactionUnavailable", "Unable to save right now. Please retry."));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await plans.LockForConsumptionAsync(cancellationToken);
            // Match application submission's lock order: user before candidate profile.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {actorUserId} FOR UPDATE", cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM candidate_profiles WHERE user_id = {actorUserId} FOR UPDATE", cancellationToken);
            var result = await SaveCoreAsync(actorUserId, jobId, cancellationToken);
            if (result.IsSuccess) await tx.CommitAsync(cancellationToken); else await tx.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return result;
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    private async Task<Result> SaveCoreAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken)
    {
        var candidate = await CandidateAsync(actorUserId, cancellationToken);
        if (!candidate.HasValue) return Result.Failure(Denied());
        var slug = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.Slug).SingleOrDefaultAsync(cancellationToken);
        if (slug is null || (await publicJobs.GetBySlugAsync(slug, cancellationToken, actorUserId)).IsFailure)
            return Result.Failure(Unavailable());
        if (await db.CandidateSavedJobs.AnyAsync(s => s.CandidateId == candidate.Value && s.JobId == jobId, cancellationToken))
            return Result.Success();
        if (plans is not null)
        {
            var limit = await plans.GetCandidateSavedJobLimitAsync(candidate.Value, cancellationToken);
            // Blocked Studio entries are hidden from the list and cannot be removed there.
            // Exclude them from usage as well; unblocking may put an account over its limit.
            var count = await (from saved in db.CandidateSavedJobs.AsNoTracking()
                join job in db.Jobs.AsNoTracking() on saved.JobId equals job.Id
                where saved.CandidateId == candidate.Value &&
                    !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Value && b.StudioId == job.StudioId)
                select saved.JobId).CountAsync(cancellationToken);
            if (limit.HasValue && count >= limit.Value)
                return Result.Failure(Error.RateLimited("SavedJobs.LimitReached", "Saved job limit reached. Remove a saved job before adding another."));
        }
        var save = new CandidateSavedJob { CandidateId = candidate.Value, JobId = jobId, CreatedAt = clock.UtcNow.ToUniversalTime() };
        db.CandidateSavedJobs.Add(save);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (plans is null && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "pk_candidate_saved_jobs" })
        {
            // Another request saved the same pair first. Do not swallow other constraint failures.
            db.Entry(save).State = EntityState.Detached;
        }
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var candidate = await CandidateAsync(actorUserId, cancellationToken);
        if (!candidate.HasValue) return Result.Failure(Denied());
        var saved = await db.CandidateSavedJobs.SingleOrDefaultAsync(s => s.CandidateId == candidate.Value && s.JobId == jobId, cancellationToken);
        if (saved is null) return Result.Success();
        db.CandidateSavedJobs.Remove(saved);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { db.Entry(saved).State = EntityState.Detached; }
        return Result.Success();
    }

    public async Task<Result<SavedJobPage>> ListAsync(Guid actorUserId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var candidate = await CandidateAsync(actorUserId, cancellationToken);
        if (!candidate.HasValue) return Result.Failure<SavedJobPage>(Denied());
        if (pageSize is < 1 or > 50) return Result.Failure<SavedJobPage>(Error.Validation("SavedJobs.InvalidPageSize", "Choose a page size from 1 to 50."));
        // Exclude blocked studios before counting or pagination, even when a job is unavailable.
        var query = from saved in db.CandidateSavedJobs.AsNoTracking()
                    join job in db.Jobs.AsNoTracking() on saved.JobId equals job.Id
                    where saved.CandidateId == candidate.Value &&
                        !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Value && b.StudioId == job.StudioId)
                    select new { saved.JobId, saved.CreatedAt, job.Slug };
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pages);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.JobId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        var entries = new List<SavedJobEntry>(rows.Length);
        foreach (var row in rows)
        {
            // Reuse the public gate on every read: never project internal entities or stale content.
            var visible = await publicJobs.GetBySlugAsync(row.Slug, cancellationToken, actorUserId);
            entries.Add(new SavedJobEntry(row.JobId, row.CreatedAt, visible.IsSuccess ? visible.Value.Summary : null));
        }
        var savedLimit = plans is null ? null : await plans.GetCandidateSavedJobLimitAsync(candidate.Value, cancellationToken);
        return Result.Success(new SavedJobPage(entries, total, page, pageSize, pages, plans is not null, savedLimit));
    }
}
