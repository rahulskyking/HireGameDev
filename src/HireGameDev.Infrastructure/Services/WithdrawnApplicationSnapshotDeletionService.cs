using System.Data;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HireGameDev.Infrastructure.Services;

// Development-only, disposable data. No legal holds, backup deletion or account erasure.
// Never register for production until privacy, legal and operational review is complete.
internal sealed class WithdrawnApplicationSnapshotDeletionService(AppDbContext db, IClock clock) : IWithdrawnApplicationSnapshotDeletionService
{
    public async Task<Result<bool>> DeleteAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return Failure(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Failure(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        if (actorUserId == Guid.Empty || applicationId == Guid.Empty) return Failure(Unavailable());
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Follow withdrawal's order; retention also locks job before application.
            if (!await LockAsync("SELECT id FROM users WHERE id = @id FOR SHARE", actorUserId, cancellationToken) ||
                !await LockAsync("SELECT id FROM candidate_profiles WHERE user_id = @id FOR UPDATE", actorUserId, cancellationToken))
                return await AbortAsync(tx, Unavailable(), cancellationToken);
            db.ChangeTracker.Clear();
            var candidateId = await (from candidate in db.CandidateProfiles.AsNoTracking()
                join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
                where user.Id == actorUserId && user.IsActive && user.DeletedAt == null && user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
                select (Guid?)candidate.Id).SingleOrDefaultAsync(cancellationToken);
            if (!candidateId.HasValue) return await AbortAsync(tx, Unavailable(), cancellationToken);
            var jobId = await db.Applications.AsNoTracking().Where(a => a.Id == applicationId && a.CandidateId == candidateId.Value)
                .Select(a => (Guid?)a.JobId).SingleOrDefaultAsync(cancellationToken);
            if (!jobId.HasValue || !await LockAsync("SELECT id FROM jobs WHERE id = @id FOR UPDATE", jobId.Value, cancellationToken) ||
                !await LockAsync("SELECT id FROM applications WHERE id = @id FOR UPDATE", applicationId, cancellationToken))
                return await AbortAsync(tx, Unavailable(), cancellationToken);
            db.ChangeTracker.Clear();
            var app = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId && a.CandidateId == candidateId.Value && a.JobId == jobId.Value, cancellationToken);
            if (app is null || app.Status != ApplicationStatus.Withdrawn) return await AbortAsync(tx, Unavailable(), cancellationToken);
            var now = clock.UtcNow.ToUniversalTime();
            var history = await db.ApplicationStatusHistory.AsNoTracking().Where(h => h.ApplicationId == applicationId)
                .OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => new { h.PreviousStatus, h.NewStatus, h.OccurredAt }).ToListAsync(cancellationToken);
            var valid = history.Count > 0 && history[0].PreviousStatus is null &&
                history[0].NewStatus == ApplicationStatus.Submitted && history[0].OccurredAt == app.SubmittedAt;
            for (var i = 1; valid && i < history.Count; i++)
            {
                var previous = history[i - 1]; var current = history[i];
                valid = current.OccurredAt > previous.OccurredAt && current.PreviousStatus == previous.NewStatus &&
                    (current.NewStatus == ApplicationStatus.Withdrawn
                        ? ApplicationLifecycleRules.CanCandidateWithdraw(previous.NewStatus)
                        : ApplicationLifecycleRules.CanStudioTransition(previous.NewStatus, current.NewStatus));
            }
            if (!valid || history[^1].NewStatus != ApplicationStatus.Withdrawn || history[^1].OccurredAt > now || app.SubmittedAt > now)
                return await AbortAsync(tx, Inconsistent(), cancellationToken);
            var snapshot = await db.ApplicationSnapshots.AsNoTracking().Where(s => s.ApplicationId == applicationId)
                .Select(s => new { s.ConsentVersion, s.ConsentedAt }).SingleOrDefaultAsync(cancellationToken);
            if (snapshot is null && app.SnapshotPurgedAt.HasValue)
            {
                if (app.SnapshotPurgedAt > now) return await AbortAsync(tx, Inconsistent(), cancellationToken);
                await tx.RollbackAsync(cancellationToken); db.ChangeTracker.Clear();
                return Result.Success(false);
            }
            if (snapshot is null || app.SnapshotPurgedAt.HasValue || string.IsNullOrWhiteSpace(app.ConsentVersion) ||
                app.ConsentedAt is null || app.ConsentedAt > now || app.ConsentVersion != snapshot.ConsentVersion || app.ConsentedAt != snapshot.ConsentedAt)
                return await AbortAsync(tx, Inconsistent(), cancellationToken);
            var deleted = await db.ApplicationSnapshots.Where(s => s.ApplicationId == applicationId).ExecuteDeleteAsync(cancellationToken);
            if (deleted != 1) return await AbortAsync(tx, Inconsistent(), cancellationToken);
            app.SnapshotPurgedAt = now;
            db.AuditLogs.Add(new AuditLog { ActorUserId = actorUserId, ActorType = AuditActorType.User,
                Action = "application.snapshot_deleted_by_candidate", EntityType = "application", EntityId = applicationId.ToString(), CreatedAt = now });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return Result.Success(true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { return await AbortAsync(tx, Conflict(), CancellationToken.None); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { return await AbortAsync(tx, Conflict(), CancellationToken.None); }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    private async Task<bool> LockAsync(string sql, Guid id, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); command.CommandText = sql;
        var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(ct) is not null;
    }
    private async Task<Result<bool>> AbortAsync(IDbContextTransaction tx, Error error, CancellationToken ct)
    { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); return Failure(error); }
    private static Result<bool> Failure(Error error) => Result.Failure<bool>(error);
    private static Error Unavailable() => Error.NotFound("Applications.Unavailable", "This application is unavailable.");
    private static Error Inconsistent() => Error.Conflict("Applications.Inconsistent", "Snapshot deletion could not be completed.");
    private static Error Conflict() => Error.Conflict("Applications.ConcurrentChange", "The application changed. Please retry.");
}
