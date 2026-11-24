using System.Data;
using HireGameDev.Application.Abstractions;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HireGameDev.Infrastructure.Services;

// Deliberately not registered or scheduled. For disposable database verification only:
// legal holds, account erasure, file cleanup, and operational controls are not implemented.
internal sealed class ApplicationRetentionService(AppDbContext db, IClock clock)
{
    internal enum Outcome { Purged, NotDue, AlreadyPurged, Inconsistent, Missing }
    internal sealed record ItemResult(Guid ApplicationId, Outcome Result);

    // A caller must supply a bounded set of IDs; no application details enter the result or logs.
    internal async Task<IReadOnlyList<ItemResult>> PurgeBatchAsync(IEnumerable<Guid> applicationIds, CancellationToken ct = default)
    {
        var ids = applicationIds.Distinct().Take(101).ToArray();
        if (ids.Length > 100) throw new ArgumentException("A batch may contain at most 100 applications.", nameof(applicationIds));
        var results = new List<ItemResult>(ids.Length);
        foreach (var id in ids)
            results.Add(new ItemResult(id, await PurgeAsync(id, ct)));
        return results;
    }

    internal async Task<Outcome> PurgeAsync(Guid id, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Retention requires PostgreSQL.");
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Retention requires a clean, independent unit of work.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var jobId = await db.Applications.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.JobId).SingleOrDefaultAsync(ct);
            if (jobId is null || !await LockAsync("SELECT id FROM jobs WHERE id = @id FOR UPDATE", jobId.Value, ct) ||
                !await LockAsync("SELECT id FROM applications WHERE id = @id FOR UPDATE", id, ct))
                return await FinishAsync(transaction, Outcome.Missing, ct);
            db.ChangeTracker.Clear();
            var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, ct);
            var app = await db.Applications.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (job is null || app is null || app.JobId != jobId)
                return await FinishAsync(transaction, Outcome.Inconsistent, ct);
            // Read only receipt metadata, never snapshot content or phone.
            var snapshot = await db.ApplicationSnapshots.AsNoTracking().Where(x => x.ApplicationId == id)
                .Select(x => new { x.ConsentVersion, x.ConsentedAt }).SingleOrDefaultAsync(ct);
            if (snapshot is null)
                return await FinishAsync(transaction, app.SnapshotPurgedAt.HasValue ? Outcome.AlreadyPurged : Outcome.Inconsistent, ct);
            if (app.SnapshotPurgedAt.HasValue || string.IsNullOrWhiteSpace(app.ConsentVersion) ||
                app.ConsentedAt is null || app.ConsentVersion != snapshot.ConsentVersion || app.ConsentedAt != snapshot.ConsentedAt)
                return await FinishAsync(transaction, Outcome.Inconsistent, ct);

            var now = clock.UtcNow.ToUniversalTime();
            var history = await db.ApplicationStatusHistory.AsNoTracking()
                .Where(x => x.ApplicationId == id).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
                .Select(x => new { x.PreviousStatus, x.NewStatus, x.OccurredAt }).ToListAsync(ct);
            var historyValid = history.Count > 0 && history[0].PreviousStatus is null &&
                history[0].NewStatus == ApplicationStatus.Submitted && history[0].OccurredAt == app.SubmittedAt;
            for (var i = 1; historyValid && i < history.Count; i++)
            {
                var previous = history[i - 1]; var current = history[i];
                historyValid = current.OccurredAt > previous.OccurredAt && current.PreviousStatus == previous.NewStatus &&
                    (current.NewStatus == ApplicationStatus.Withdrawn
                        ? ApplicationLifecycleRules.CanCandidateWithdraw(previous.NewStatus)
                        : ApplicationLifecycleRules.CanStudioTransition(previous.NewStatus, current.NewStatus));
            }
            if (!historyValid || history[^1].NewStatus != app.Status || history[^1].OccurredAt > now ||
                app.SubmittedAt > now || app.ConsentedAt > now ||
                !Enum.IsDefined(app.Status) || !Enum.IsDefined(job.Status) || job.Status == JobStatus.Draft ||
                (job.Status == JobStatus.Closed && !job.ClosedAt.HasValue) ||
                (job.Status == JobStatus.Removed && !job.RemovedAt.HasValue) ||
                (job.Status == JobStatus.Expired && !job.ExpiresAt.HasValue) ||
                job.ClosedAt > now || job.RemovedAt > now ||
                (job.Status != JobStatus.Closed && job.Status != JobStatus.Removed && (job.ClosedAt.HasValue || job.RemovedAt.HasValue)) ||
                (job.Status == JobStatus.Closed && job.RemovedAt.HasValue) ||
                (job.ClosedAt.HasValue && job.RemovedAt.HasValue && job.ClosedAt > job.RemovedAt))
                return await FinishAsync(transaction, Outcome.Inconsistent, ct);
            var withdrawnAt = app.Status == ApplicationStatus.Withdrawn ? history[^1].OccurredAt : (DateTimeOffset?)null;
            var due = ApplicationRetentionRules.SnapshotDeletionDueAt(app.Status, withdrawnAt,
                job.Status, job.ExpiresAt, job.ClosedAt, job.RemovedAt, now);
            if (due is null || due > now) return await FinishAsync(transaction, Outcome.NotDue, ct);

            var deleted = await db.ApplicationSnapshots.Where(x => x.ApplicationId == id).ExecuteDeleteAsync(ct);
            if (deleted != 1) throw new InvalidOperationException("Snapshot changed during retention operation.");
            app.SnapshotPurgedAt = now;
            db.AuditLogs.Add(new AuditLog { ActorType = AuditActorType.System, Action = "application.snapshot_purged",
                EntityType = "application", EntityId = id.ToString(), CreatedAt = now });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return Outcome.Purged;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<bool> LockAsync(string sql, Guid id, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private async Task<Outcome> FinishAsync(IDbContextTransaction transaction, Outcome result, CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);
        db.ChangeTracker.Clear();
        return result;
    }
}
