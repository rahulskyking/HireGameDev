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

// Prototype only; no applicant readers/routes. Preserve lock coordination before exposure.
internal sealed class StudioApplicationStatusService(AppDbContext db, IClock clock) : IStudioApplicationStatusService
{
    public async Task<Result> ChangeStatusAsync(Guid actorUserId, Guid applicationId, ApplicationStatus newStatus, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return Result.Failure(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        if (actorUserId == Guid.Empty || applicationId == Guid.Empty || !Enum.IsDefined(newStatus)) return Result.Failure(Unavailable());
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Identifiers here are advisory only. All are rechecked after locks.
            var target = await (from a in db.Applications.AsNoTracking() join j in db.Jobs.AsNoTracking() on a.JobId equals j.Id
                join c in db.CandidateProfiles.AsNoTracking() on a.CandidateId equals c.Id
                where a.Id == applicationId select new { a.JobId, a.CandidateId, CandidateUserId = c.UserId, j.StudioId }).SingleOrDefaultAsync(cancellationToken);
            if (target is null ||
                !await LockAsync("SELECT id FROM studio_profiles WHERE id = @id FOR SHARE", target.StudioId, null, cancellationToken) ||
                !await LockAsync("SELECT id FROM users WHERE id = @id FOR SHARE", actorUserId, null, cancellationToken) ||
                !await LockAsync("SELECT id FROM users WHERE id = @id FOR SHARE", target.CandidateUserId, null, cancellationToken) ||
                !await LockAsync("SELECT user_id FROM studio_members WHERE studio_id = @id AND user_id = @other FOR SHARE", target.StudioId, actorUserId, cancellationToken) ||
                !await LockAsync("SELECT id FROM candidate_profiles WHERE id = @id FOR UPDATE", target.CandidateId, null, cancellationToken) ||
                !await LockAsync("SELECT id FROM jobs WHERE id = @id FOR UPDATE", target.JobId, null, cancellationToken) ||
                !await LockAsync("SELECT id FROM applications WHERE id = @id FOR UPDATE", applicationId, null, cancellationToken))
                return await AbortAsync(tx, Unavailable(), cancellationToken);
            db.ChangeTracker.Clear();
            var allowed = await (from studio in db.StudioProfiles.AsNoTracking()
                join member in db.StudioMembers.AsNoTracking() on studio.Id equals member.StudioId
                join user in db.Users.AsNoTracking() on member.UserId equals user.Id
                where studio.Id == target.StudioId && studio.DeletedAt == null && studio.SuspendedAt == null &&
                    studio.VerificationStatus == StudioVerificationStatus.Verified && user.Id == actorUserId &&
                    user.IsActive && user.DeletedAt == null && user.AccountType == AccountType.Studio &&
                    (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter)
                select studio.Id).AnyAsync(cancellationToken);
            var candidateActive = await (from c in db.CandidateProfiles.AsNoTracking() join u in db.Users.AsNoTracking() on c.UserId equals u.Id
                where c.Id == target.CandidateId && c.UserId == target.CandidateUserId && c.DeletedAt == null && u.IsActive && u.DeletedAt == null && u.AccountType == AccountType.Candidate
                select c.Id).AnyAsync(cancellationToken);
            var blocked = await db.CandidateStudioBlocks.AsNoTracking().AnyAsync(b => b.CandidateId == target.CandidateId && b.StudioId == target.StudioId, cancellationToken);
            var jobValid = await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == target.JobId && j.StudioId == target.StudioId, cancellationToken);
            var app = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId && a.JobId == target.JobId && a.CandidateId == target.CandidateId, cancellationToken);
            if (!allowed || !candidateActive || blocked || !jobValid || app is null || app.Status == ApplicationStatus.Withdrawn || app.SnapshotPurgedAt != null)
                return await AbortAsync(tx, Unavailable(), cancellationToken);
            if (!await db.ApplicationSnapshots.AsNoTracking().AnyAsync(s => s.ApplicationId == applicationId, cancellationToken))
                return await AbortAsync(tx, Unavailable(), cancellationToken);
            if (!ApplicationLifecycleRules.CanStudioTransition(app.Status, newStatus))
                return await AbortAsync(tx, Error.Conflict("Applications.InvalidTransition", "This status change is not allowed."), cancellationToken);
            var last = await db.ApplicationStatusHistory.AsNoTracking().Where(h => h.ApplicationId == applicationId)
                .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).FirstOrDefaultAsync(cancellationToken);
            var now = clock.UtcNow.ToUniversalTime(); now = now.AddTicks(-(now.Ticks % 10));
            if (last is null || last.NewStatus != app.Status || last.OccurredAt >= now)
                return await AbortAsync(tx, Error.Conflict("Applications.Inconsistent", "The application could not be updated. Please retry."), cancellationToken);
            var previous = app.Status; app.Status = newStatus; app.UpdatedAt = now;
            db.ApplicationStatusHistory.Add(new ApplicationStatusHistory { Id = Guid.NewGuid(), ApplicationId = applicationId,
                PreviousStatus = previous, NewStatus = newStatus, ActorUserId = actorUserId, OccurredAt = now });
            await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); db.ChangeTracker.Clear();
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { return await AbortAsync(tx, Conflict(), CancellationToken.None); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { return await AbortAsync(tx, Conflict(), CancellationToken.None); }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    private async Task<bool> LockAsync(string sql, Guid id, Guid? other, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); command.CommandText = sql;
        var p = command.CreateParameter(); p.ParameterName = "id"; p.Value = id; command.Parameters.Add(p);
        if (other.HasValue) { var q = command.CreateParameter(); q.ParameterName = "other"; q.Value = other.Value; command.Parameters.Add(q); }
        return await command.ExecuteScalarAsync(ct) is not null;
    }
    private async Task<Result> AbortAsync(IDbContextTransaction tx, Error error, CancellationToken ct)
    { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); return Result.Failure(error); }
    private static Error Unavailable() => Error.NotFound("Applications.Unavailable", "This application is unavailable.");
    private static Error Conflict() => Error.Conflict("Applications.ConcurrentChange", "The application changed. Please retry.");
}
