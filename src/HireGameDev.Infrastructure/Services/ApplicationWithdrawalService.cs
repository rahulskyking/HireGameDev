using System.Data;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HireGameDev.Infrastructure.Services;

// Backend prototype only. Reads, routes and public exposure need their own privacy and concurrency gates.
internal sealed class ApplicationWithdrawalService(AppDbContext db, IClock clock) : IApplicationWithdrawalService
{
    public async Task<Result<ApplicationWithdrawalResult>> WithdrawAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return Fail(Error.Failure("Applications.PostgresRequired", "Withdrawal requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Fail(Error.Conflict("Applications.UnitOfWorkBusy", "Withdrawal requires a clean, independent unit of work."));
        if (actorUserId == Guid.Empty || applicationId == Guid.Empty) return Fail(Unavailable());
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Match submission's user/Candidate order. Job then application matches retention.
            if (!await LockAsync("SELECT id FROM users WHERE id = @id FOR SHARE", actorUserId, cancellationToken) ||
                !await LockAsync("SELECT id FROM candidate_profiles WHERE user_id = @id FOR UPDATE", actorUserId, cancellationToken))
                return await AbortAsync(tx, Unavailable(), cancellationToken);
            db.ChangeTracker.Clear();
            var candidateId = await (from candidate in db.CandidateProfiles.AsNoTracking()
                join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
                where user.Id == actorUserId && user.IsActive && user.DeletedAt == null &&
                    user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
                select (Guid?)candidate.Id).SingleOrDefaultAsync(cancellationToken);
            if (candidateId is null) return await AbortAsync(tx, Unavailable(), cancellationToken);
            var jobId = await db.Applications.AsNoTracking().Where(a => a.Id == applicationId && a.CandidateId == candidateId)
                .Select(a => (Guid?)a.JobId).SingleOrDefaultAsync(cancellationToken);
            if (jobId is null || !await LockAsync("SELECT id FROM jobs WHERE id = @id FOR UPDATE", jobId.Value, cancellationToken) ||
                !await LockAsync("SELECT id FROM applications WHERE id = @id FOR UPDATE", applicationId, cancellationToken))
                return await AbortAsync(tx, Unavailable(), cancellationToken);
            db.ChangeTracker.Clear();
            var app = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId && a.CandidateId == candidateId && a.JobId == jobId, cancellationToken);
            var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (app is null || job is null) return await AbortAsync(tx, Unavailable(), cancellationToken);
            if (app.Status == ApplicationStatus.Withdrawn)
            {
                await tx.RollbackAsync(cancellationToken); db.ChangeTracker.Clear();
                return Result.Success(new ApplicationWithdrawalResult(false));
            }
            if (!ApplicationLifecycleRules.CanCandidateWithdraw(app.Status))
                return await AbortAsync(tx, Error.Conflict("Applications.CannotWithdraw", "This application cannot be withdrawn."), cancellationToken);
            if (job.ApplicationsCount <= 0)
                return await AbortAsync(tx, Error.Conflict("Applications.Inconsistent", "The application could not be updated."), cancellationToken);
            var last = await db.ApplicationStatusHistory.AsNoTracking().Where(h => h.ApplicationId == applicationId)
                .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).FirstOrDefaultAsync(cancellationToken);
            var now = clock.UtcNow.ToUniversalTime();
            // PostgreSQL stores microseconds. Equal timestamps would make retention history order ambiguous.
            now = now.AddTicks(-(now.Ticks % 10));
            if (last is null || last.NewStatus != app.Status || last.OccurredAt >= now)
                return await AbortAsync(tx, Error.Conflict("Applications.Inconsistent", "The application could not be updated. Please retry."), cancellationToken);
            var previous = app.Status;
            app.Status = ApplicationStatus.Withdrawn; app.UpdatedAt = now;
            job.ApplicationsCount--;
            db.ApplicationStatusHistory.Add(new ApplicationStatusHistory { Id = Guid.NewGuid(), ApplicationId = applicationId,
                PreviousStatus = previous, NewStatus = ApplicationStatus.Withdrawn, ActorUserId = actorUserId, OccurredAt = now });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return Result.Success(new ApplicationWithdrawalResult(true));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { return await AbortAsync(tx, Conflict(), CancellationToken.None); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { return await AbortAsync(tx, Conflict(), CancellationToken.None); }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw;
        }
    }

    private async Task<bool> LockAsync(string sql, Guid id, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); command.CommandText = sql;
        var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(ct) is not null;
    }
    private async Task<Result<ApplicationWithdrawalResult>> AbortAsync(IDbContextTransaction tx, Error error, CancellationToken ct)
    { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); return Fail(error); }
    private static Result<ApplicationWithdrawalResult> Fail(Error error) => Result.Failure<ApplicationWithdrawalResult>(error);
    private static Error Unavailable() => Error.NotFound("Applications.Unavailable", "This application is unavailable.");
    private static Error Conflict() => Error.Conflict("Applications.ConcurrentChange", "The application changed. Please retry.");
}
