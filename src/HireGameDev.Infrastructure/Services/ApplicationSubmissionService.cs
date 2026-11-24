using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HireGameDev.Infrastructure.Services;

/// <summary>
/// PostgreSQL-only submission. Candidate data uses a per-Candidate lock coordinated by database triggers.
/// Referenced city and skill rows are locked individually. Do not expose for production
/// until concurrency, deadlock/load testing and privacy/retention policy are reviewed.
/// </summary>
internal sealed class ApplicationSubmissionService : IApplicationSubmissionService
{
    public const string ConsentVersion = "application-share-v1";
    public const string ConsentText = "I consent to sharing the reviewed application information with this studio. I understand it is saved as a snapshot, may include personal or confidential information in my text and links, and that phone is shown after shortlist.";
    private const int TokenMinutes = 15;
    private readonly AppDbContext _db;
    private readonly IApplicationReviewService _review;
    private readonly IClock _clock;
    private readonly IDataProtector _protector;

    public ApplicationSubmissionService(AppDbContext db, IApplicationReviewService review, IClock clock, IDataProtectionProvider dataProtection)
    {
        _db = db; _review = review; _clock = clock;
        _protector = dataProtection.CreateProtector("HireGameDev.Applications.Review.v1");
    }

    public async Task<Result<ApplicationReviewOffer>> PrepareAsync(Guid actorUserId, Guid jobId, string? screeningAnswer, CancellationToken cancellationToken = default)
    {
        var result = await _review.ReviewAsync(actorUserId, jobId, screeningAnswer, cancellationToken);
        if (result.IsFailure) return Result.Failure<ApplicationReviewOffer>(result.Error);
        var expires = _clock.UtcNow.ToUniversalTime().AddMinutes(TokenMinutes);
        var payload = new ReviewPayload(actorUserId, jobId, ConsentVersion, expires, Fingerprint(result.Value));
        var token = _protector.Protect(JsonSerializer.Serialize(payload));
        return Result.Success(new ApplicationReviewOffer(result.Value, token, ConsentVersion, ConsentText, expires));
    }

    public async Task<Result<SubmittedApplication>> SubmitAsync(SubmitApplicationCommand command, CancellationToken cancellationToken = default)
    {
        if (!command.Consent) return Result.Failure<SubmittedApplication>(Error.Validation("Applications.ConsentRequired", "Review and agree before applying."));
        if (string.IsNullOrWhiteSpace(command.ReviewToken) || command.ReviewToken.Length > 8192)
            return Result.Failure<SubmittedApplication>(Stale());
        ReviewPayload? payload;
        try { payload = JsonSerializer.Deserialize<ReviewPayload>(_protector.Unprotect(command.ReviewToken)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        { return Result.Failure<SubmittedApplication>(Stale()); }
        if (payload is null || payload.ActorUserId != command.ActorUserId || payload.JobId != command.JobId ||
            payload.ConsentVersion != ConsentVersion || payload.ExpiresAt <= _clock.UtcNow || string.IsNullOrEmpty(payload.Fingerprint))
            return Result.Failure<SubmittedApplication>(Stale());
        // No InMemory fallback: its ignored transactions/locks cannot prove capacity or consent safety.
        if (!_db.Database.IsNpgsql()) return Result.Failure<SubmittedApplication>(Error.Failure("Applications.PostgresRequired", "Submission requires PostgreSQL."));
        if (_db.Database.CurrentTransaction is not null || _db.ChangeTracker.HasChanges())
            return Result.Failure<SubmittedApplication>(Error.Conflict("Applications.UnitOfWorkBusy", "Submission requires a clean, independent unit of work."));

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var studioId = await _db.Jobs.AsNoTracking().Where(j => j.Id == command.JobId)
                .Select(j => (Guid?)j.StudioId).SingleOrDefaultAsync(cancellationToken);
            if (!studioId.HasValue || !await LockRowAsync("SELECT id FROM studio_profiles WHERE id = @id FOR SHARE", studioId.Value, cancellationToken))
                return await AbortAsync(transaction, Error.NotFound("Applications.Unavailable", "This listing is unavailable."), cancellationToken);
            if (!await LockRowAsync("SELECT id FROM users WHERE id = @id FOR SHARE", command.ActorUserId, cancellationToken) ||
                !await LockRowAsync("SELECT id FROM candidate_profiles WHERE user_id = @id FOR UPDATE", command.ActorUserId, cancellationToken))
                return await AbortAsync(transaction, Error.Forbidden("Applications.CandidateRequired", "An active Candidate account is required."), cancellationToken);
            // The Candidate lock freezes profile references and trigger-protected child rows.
            // Lock each referenced name, including portfolio skills and shipped game engines,
            // before reloading the reviewed snapshot. Unrelated reference rows remain writable.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM cities WHERE id = (SELECT city_id FROM candidate_profiles WHERE user_id = {command.ActorUserId}) FOR SHARE", cancellationToken);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT id FROM skills WHERE id IN (
                    SELECT cs.skill_id FROM candidate_skills cs JOIN candidate_profiles c ON c.id = cs.candidate_id WHERE c.user_id = {command.ActorUserId}
                    UNION SELECT pls.skill_id FROM portfolio_link_skills pls JOIN portfolio_links pl ON pl.id = pls.portfolio_link_id JOIN candidate_profiles c ON c.id = pl.candidate_id WHERE c.user_id = {command.ActorUserId}
                    UNION SELECT st.engine_skill_id FROM shipped_titles st JOIN candidate_profiles c ON c.id = st.candidate_id WHERE c.user_id = {command.ActorUserId} AND st.engine_skill_id IS NOT NULL
                ) ORDER BY id FOR SHARE
                """, cancellationToken);
            // Studio precedes Job, matching JobLifecycleService. The job's studio is read before
            // locking and checked again after the job lock, so a concurrent reassignment fails closed.
            await LockJobAsync(command.JobId, cancellationToken);
            var lockedStudioId = await _db.Jobs.AsNoTracking().Where(j => j.Id == command.JobId)
                .Select(j => (Guid?)j.StudioId).SingleOrDefaultAsync(cancellationToken);
            if (lockedStudioId != studioId)
                return await AbortAsync(transaction, Error.Conflict("Applications.ConcurrentChange", "The application changed. Please review and retry."), cancellationToken);
            // A context may already track stale entities from an earlier request; start fresh after locking.
            _db.ChangeTracker.Clear();
            var fresh = await _review.ReviewAsync(command.ActorUserId, command.JobId, command.ScreeningAnswer, cancellationToken);
            if (fresh.IsFailure) return await AbortAsync(transaction, fresh.Error, cancellationToken);
            if (payload.ExpiresAt <= _clock.UtcNow || !FixedHashEquals(payload.Fingerprint, Fingerprint(fresh.Value)))
                return await AbortAsync(transaction, Stale(), cancellationToken);
            var candidate = await _db.CandidateProfiles.AsNoTracking().SingleAsync(x => x.UserId == command.ActorUserId && x.DeletedAt == null, cancellationToken);
            var job = await _db.Jobs.SingleAsync(x => x.Id == command.JobId, cancellationToken);
            // Recheck time at the final point: waiting for locks or snapshot loading may span expiry.
            var now = _clock.UtcNow.ToUniversalTime();
            if (job.Status != JobStatus.Active || !job.ExpiresAt.HasValue || job.ExpiresAt <= now || job.ApplicationsCount >= job.ApplicationCap)
                return await AbortAsync(transaction, Error.Conflict("Applications.NotEligible", "You cannot submit a new application for this job."), cancellationToken);
            var applicationId = Guid.NewGuid();
            _db.Applications.Add(new JobApplication { Id = applicationId, CandidateId = candidate.Id, JobId = job.Id, Status = ApplicationStatus.Submitted, SubmittedAt = now, UpdatedAt = now, ConsentVersion = ConsentVersion, ConsentedAt = now });
            _db.ApplicationSnapshots.Add(new ApplicationSnapshot { ApplicationId = applicationId, SnapshotVersion = fresh.Value.Version,
                CandidateData = JsonSerializer.Serialize(fresh.Value.Candidate), JobData = JsonSerializer.Serialize(fresh.Value.Job), Phone = fresh.Value.Phone,
                ConsentVersion = ConsentVersion, ConsentedAt = now });
            _db.ApplicationStatusHistory.Add(new ApplicationStatusHistory { Id = Guid.NewGuid(), ApplicationId = applicationId,
                NewStatus = ApplicationStatus.Submitted, ActorUserId = command.ActorUserId, OccurredAt = now });
            job.ApplicationsCount++;
            if (job.ApplicationsCount >= job.ApplicationCap)
            {
                job.Status = JobStatus.Full;
                _db.JobStatusHistory.Add(new JobStatusHistory { Id = Guid.NewGuid(), JobId = job.Id, PreviousStatus = JobStatus.Active,
                    NewStatus = JobStatus.Full, ActorType = JobHistoryActorType.User, ActorUserId = command.ActorUserId,
                    ReasonCode = "application_cap_reached", OccurredAt = now });
            }
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            return Result.Success(new SubmittedApplication(applicationId, now));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_applications_candidate_job" })
        { return await AbortAsync(transaction, Error.Conflict("Applications.AlreadyApplied", "You have already applied for this job."), cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { return await AbortAsync(transaction, Error.Conflict("Applications.ConcurrentChange", "The application changed. Please review and retry."), cancellationToken); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        { return await AbortAsync(transaction, Error.Conflict("Applications.ConcurrentChange", "The application changed. Please review and retry."), cancellationToken); }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<bool> LockRowAsync(string sql, Guid id, CancellationToken ct)
    {
        // Callers supply fixed SQL literals; the ID is always a parameter.
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(ct) is not null;
    }
    private async Task LockJobAsync(Guid jobId, CancellationToken ct)
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT 1 FROM jobs WHERE id = @id FOR UPDATE";
        var id = command.CreateParameter(); id.ParameterName = "id"; id.Value = jobId; command.Parameters.Add(id);
        await command.ExecuteScalarAsync(ct);
    }
    private async Task<Result<SubmittedApplication>> AbortAsync(IDbContextTransaction transaction, Error error, CancellationToken ct)
    {
        await transaction.RollbackAsync(ct);
        _db.ChangeTracker.Clear();
        return Result.Failure<SubmittedApplication>(error);
    }
    private static string Fingerprint(ApplicationSnapshotDraft snapshot) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
    private static bool FixedHashEquals(string a, string b) => CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
    private static Error Stale() => Error.Conflict("Applications.ReviewStale", "Review the application again before submitting.");
    private sealed record ReviewPayload(Guid ActorUserId, Guid JobId, string ConsentVersion, DateTimeOffset ExpiresAt, string Fingerprint);
}
