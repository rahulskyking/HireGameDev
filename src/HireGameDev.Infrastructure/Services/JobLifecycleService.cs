using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HireGameDev.Infrastructure.Services;

internal sealed class JobLifecycleService : IJobLifecycleService
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly IJobPublishChecker _checker;
    private readonly IAuditLogger _audit;
    private readonly JobLifecycleOptions _limits;

    public JobLifecycleService(AppDbContext db, IClock clock, IJobPublishChecker checker,
        IAuditLogger audit, IOptions<JobLifecycleOptions> options)
    {
        _db = db;
        _clock = clock;
        _checker = checker;
        _audit = audit;
        _limits = options.Value;
    }

    public async Task<Result<JobSnapshot>> GetAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(actorUserId, studioId, false, cancellationToken))
            return Result.Failure<JobSnapshot>(Denied());
        var job = await _db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId && x.StudioId == studioId, cancellationToken);
        return job is null ? Result.Failure<JobSnapshot>(Missing()) : Result.Success(await SnapshotAsync(job, cancellationToken));
    }

    public Task<Result<JobSnapshot>> SaveAsync(SaveJobCommand command, CancellationToken cancellationToken = default) =>
        MutateAsync(command.StudioId, async () =>
        {
            if (!await HasAccessAsync(command.ActorUserId, command.StudioId, true, cancellationToken))
                return Result.Failure<JobSnapshot>(Denied());
            var validation = ValidateDraft(command.Content);
            if (validation is not null) return Result.Failure<JobSnapshot>(validation);
            if (command.Content.CityId.HasValue && !await _db.Cities.AnyAsync(x => x.Id == command.Content.CityId && x.IsActive, cancellationToken))
                return Result.Failure<JobSnapshot>(Invalid("City", "Select an active city."));
            var skillIds = command.Content.Skills.Select(x => x.SkillId).ToArray();
            if (await _db.Skills.CountAsync(x => skillIds.Contains(x.Id) && x.IsActive, cancellationToken) != skillIds.Length)
                return Result.Failure<JobSnapshot>(Invalid("Skills", "Select active skills."));

            Job job;
            if (command.JobId.HasValue)
            {
                var existing = await LoadLockedJobAsync(command.JobId.Value, command.StudioId, cancellationToken);
                if (existing is null) return Result.Failure<JobSnapshot>(Missing());
                job = existing;
                if (!JobLifecycleRules.CanEdit(job) || JobLifecycleRules.NeedsExpiry(job, Now))
                    return Result.Failure<JobSnapshot>(Invalid("NotEditable", "Only current drafts and non-expired paused jobs can be edited."));
                if (!command.ExpectedVersion.HasValue || command.ExpectedVersion != job.Version)
                    return Result.Failure<JobSnapshot>(Stale());
            }
            else
            {
                if (await _db.Jobs.CountAsync(x => x.StudioId == command.StudioId && x.Status == JobStatus.Draft, cancellationToken) >= _limits.MaxDraftsPerStudio)
                    return Result.Failure<JobSnapshot>(Error.RateLimited("Jobs.DraftLimit", "Draft limit reached. Delete unused drafts first."));
                job = new Job { Id = Guid.NewGuid(), StudioId = command.StudioId, PostedByUserId = command.ActorUserId };
                job.Slug = $"job-{job.Id:N}";
                _db.Jobs.Add(job);
                AddHistory(job, null, JobStatus.Draft, command.ActorUserId, "draft_created");
            }
            // A paused listing must remain structurally complete to satisfy its database invariants.
            if (job.Status == JobStatus.Paused && !IsStructurallyComplete(command.Content))
                return Result.Failure<JobSnapshot>(Invalid("IncompletePausedJob", "Paused published content must remain complete."));
            ApplyContent(job, command.Content);
            var previousSkills = await _db.JobSkills.Where(x => x.JobId == job.Id).ToListAsync(cancellationToken);
            var requested = command.Content.Skills.ToDictionary(x => x.SkillId);
            foreach (var old in previousSkills)
            {
                if (!requested.ContainsKey(old.SkillId)) _db.JobSkills.Remove(old);
            }
            foreach (var skill in command.Content.Skills)
            {
                var row = previousSkills.SingleOrDefault(x => x.SkillId == skill.SkillId);
                if (row is null)
                {
                    row = new JobSkill { JobId = job.Id, SkillId = skill.SkillId };
                    _db.JobSkills.Add(row);
                }
                row.IsRequired = skill.IsRequired;
                row.MinProficiency = skill.MinProficiency;
            }
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(await SnapshotAsync(job, cancellationToken));
        }, cancellationToken);

    public async Task<Result> DeleteDraftAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default) =>
        await MutateAsync(studioId, async () =>
        {
            if (!await HasAccessAsync(actorUserId, studioId, true, cancellationToken)) return Result.Failure<bool>(Denied());
            var job = await LoadLockedJobAsync(jobId, studioId, cancellationToken);
            if (job is null) return Result.Failure<bool>(Missing());
            if (!JobLifecycleRules.CanDelete(job)) return Result.Failure<bool>(Invalid("CannotDelete", "Only never-published drafts can be permanently deleted."));
            _db.JobSkills.RemoveRange(await _db.JobSkills.Where(x => x.JobId == job.Id).ToListAsync(cancellationToken));
            _db.JobStatusHistory.RemoveRange(await _db.JobStatusHistory.Where(x => x.JobId == job.Id).ToListAsync(cancellationToken));
            _db.Jobs.Remove(job);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(true);
        }, cancellationToken);

    public Task<Result<JobLifecycleOutcome>> TransitionAsync(JobTransitionCommand command, CancellationToken cancellationToken = default) =>
        MutateAsync(command.StudioId, async () =>
        {
            if (!await HasAccessAsync(command.ActorUserId, command.StudioId, true, cancellationToken))
                return Result.Failure<JobLifecycleOutcome>(Denied());
            var job = await LoadLockedJobAsync(command.JobId, command.StudioId, cancellationToken);
            if (job is null) return Result.Failure<JobLifecycleOutcome>(Missing());
            if (command.ExpectedVersion.HasValue && command.ExpectedVersion != job.Version)
                return Result.Failure<JobLifecycleOutcome>(Stale());
            var studio = await _db.StudioProfiles.SingleAsync(x => x.Id == job.StudioId, cancellationToken);
            var now = Now;
            if (JobLifecycleRules.NeedsExpiry(job, now))
            {
                ChangeStatus(job, JobStatus.Expired, null, "validity_expired");
                await _db.SaveChangesAsync(cancellationToken);
            }
            // An exact retry of a successful publish is a no-op, not another quota event.
            if (command.Action == JobLifecycleAction.Publish && job.Status == JobStatus.Active && Eligible(studio))
                return Result.Success(new JobLifecycleOutcome(true, await SnapshotAsync(job, cancellationToken), Array.Empty<JobPublishFinding>()));

            var activation = command.Action is JobLifecycleAction.Publish or JobLifecycleAction.Resume or JobLifecycleAction.ReopenFull or JobLifecycleAction.Renew;
            var valid = command.Action switch
            {
                JobLifecycleAction.Publish => job.Status == JobStatus.Draft,
                JobLifecycleAction.Pause => job.Status == JobStatus.Active,
                JobLifecycleAction.Resume => job.Status == JobStatus.Paused,
                JobLifecycleAction.ReopenFull => job.Status == JobStatus.Full,
                JobLifecycleAction.Renew => job.Status == JobStatus.Expired,
                JobLifecycleAction.Close => job.Status is JobStatus.Draft or JobStatus.Active or JobStatus.Paused or JobStatus.Full or JobStatus.Expired,
                _ => false
            };
            if (!valid)
                return Result.Success(await RejectedAsync(job, "Jobs.InvalidTransition", "This action is not valid for the current job status.", cancellationToken));

            IReadOnlyList<JobPublishFinding> findings = Array.Empty<JobPublishFinding>();
            if (activation)
            {
                if (!Eligible(studio)) return Result.Success(await RejectedAsync(job, "Jobs.StudioRestricted", "A verified, non-suspended studio is required to publish or reopen jobs.", cancellationToken));
                if (job.ApplicationsCount >= job.ApplicationCap)
                    return Result.Success(await RejectedAsync(job, "Jobs.CapacityReached", "Application capacity must be available before activation.", cancellationToken));
                var content = (await SnapshotAsync(job, cancellationToken)).Content;
                var checkedContent = await _checker.CheckAsync(new(job.Id, job.StudioId, content), cancellationToken);
                if (!checkedContent.CanPublish)
                    return Result.Success(new JobLifecycleOutcome(false, await SnapshotAsync(job, cancellationToken), checkedContent.Findings));
                if (!IsStructurallyComplete(content))
                    return Result.Success(await RejectedAsync(job, "Jobs.Incomplete", "Complete mandatory fields before publishing.", cancellationToken));
                findings = checkedContent.Findings;
                ApplyFingerprint(job, content);
                var publishing = command.Action is JobLifecycleAction.Publish or JobLifecycleAction.Renew;
                var quotaFailure = await CheckActivationLimitsAsync(job, publishing, now, cancellationToken);
                if (quotaFailure is not null)
                    return Result.Success(await RejectedAsync(job, quotaFailure.Code, quotaFailure.Message, cancellationToken));
                if (publishing)
                {
                    job.FirstPublishedAt ??= now;
                    job.PublishedAt = now;
                    job.ExpiresAt = now.AddDays(_limits.ValidityDays);
                    _db.JobPublicationEvents.Add(new JobPublicationEvent
                    {
                        Id = Guid.NewGuid(), JobId = job.Id, StudioId = job.StudioId, ActorUserId = command.ActorUserId,
                        Kind = command.Action == JobLifecycleAction.Publish ? JobPublicationKind.Initial : JobPublicationKind.Renewal,
                        NormalizedTitle = job.NormalizedTitle!, ContentFingerprint = job.ContentFingerprint!, PublishedAt = now
                    });
                }
                ChangeStatus(job, JobStatus.Active, command.ActorUserId, command.Action switch
                {
                    JobLifecycleAction.Publish => "studio_published", JobLifecycleAction.Renew => "studio_renewed",
                    JobLifecycleAction.ReopenFull => "studio_reopened", _ => "studio_resumed"
                });
            }
            else if (command.Action == JobLifecycleAction.Pause) ChangeStatus(job, JobStatus.Paused, command.ActorUserId, "studio_paused");
            else
            {
                job.ClosedAt = now;
                ChangeStatus(job, JobStatus.Closed, command.ActorUserId, "studio_closed");
            }
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(new JobLifecycleOutcome(true, await SnapshotAsync(job, cancellationToken), findings));
        }, cancellationToken);

    public async Task<Result> RemoveAsync(RemoveJobCommand command, CancellationToken cancellationToken = default)
    {
        // Resolve the Studio from the job itself, never from an Admin/client-supplied ownership assertion.
        var studioId = await _db.Jobs.AsNoTracking().Where(x => x.Id == command.JobId).Select(x => (Guid?)x.StudioId).SingleOrDefaultAsync(cancellationToken);
        if (!studioId.HasValue) return Result.Failure(Missing());
        return await MutateAsync(studioId.Value, async () =>
        {
            if (!await IsAdminAsync(command.ActorUserId, cancellationToken)) return Result.Failure<bool>(Denied());
            if (!ValidReason(command.Reason)) return Result.Failure<bool>(Invalid("ReasonRequired", "Provide a reason between 1 and 1000 characters."));
            var job = await LoadLockedJobAsync(command.JobId, studioId.Value, cancellationToken);
            if (job is null) return Result.Failure<bool>(Missing());
            if (!job.FirstPublishedAt.HasValue) return Result.Failure<bool>(Invalid("NotPublished", "Only previously published jobs can be removed."));
            if (job.Status == JobStatus.Removed) return Result.Success(true);
            job.RemovedAt = Now;
            job.RemovedByUserId = command.ActorUserId;
            job.RemovalReason = command.Reason.Trim();
            ChangeStatus(job, JobStatus.Removed, command.ActorUserId, "admin_removed", job.RemovalReason);
            Audit(command.ActorUserId, "job.removed", "job", job.Id, command.Reason);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(true);
        }, cancellationToken);
    }

    public async Task<Result> RestrictStudioAsync(StudioJobRestrictionCommand command, CancellationToken cancellationToken = default) =>
        await MutateAsync(command.StudioId, async () =>
        {
            if (!await IsAdminAsync(command.ActorUserId, cancellationToken)) return Result.Failure<bool>(Denied());
            if (!ValidReason(command.Reason) || !Enum.IsDefined(command.Action))
                return Result.Failure<bool>(Invalid("InvalidRestriction", "Provide a valid restriction and a reason between 1 and 1000 characters."));
            var studio = await _db.StudioProfiles.SingleOrDefaultAsync(x => x.Id == command.StudioId && x.DeletedAt == null, cancellationToken);
            if (studio is null) return Result.Failure<bool>(Missing());
            if (command.Action == StudioJobRestrictionAction.Suspend)
            {
                studio.SuspendedAt = Now;
                studio.SuspendedByUserId = command.ActorUserId;
                studio.SuspensionReason = command.Reason.Trim();
            }
            else if (command.Action == StudioJobRestrictionAction.LiftSuspension)
            {
                studio.SuspendedAt = null;
                studio.SuspendedByUserId = null;
                studio.SuspensionReason = null;
            }
            if (command.Action == StudioJobRestrictionAction.Suspend)
                await JobRestrictionTransitions.PauseActiveJobsAsync(_db, studio.Id, command.ActorUserId,
                    Now, "studio_suspended", command.Reason.Trim(), cancellationToken);
            Audit(command.ActorUserId, command.Action == StudioJobRestrictionAction.Suspend ? "studio.suspended" : "studio.suspension_lifted", "studio", studio.Id, command.Reason);
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(true);
        }, cancellationToken);

    public async Task<int> ExpireDueJobsAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var now = Now;
        var candidates = await _db.Jobs.AsNoTracking()
            .Where(x => (x.Status == JobStatus.Active || x.Status == JobStatus.Paused || x.Status == JobStatus.Full) && x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id).Take(Math.Clamp(batchSize, 1, 1000))
            .Select(x => new { x.Id, x.StudioId }).ToArrayAsync(cancellationToken);
        var count = 0;
        foreach (var candidate in candidates)
        {
            var result = await MutateAsync(candidate.StudioId, async () =>
            {
                var job = await LoadLockedJobAsync(candidate.Id, candidate.StudioId, cancellationToken);
                if (job is null || !JobLifecycleRules.NeedsExpiry(job, Now)) return Result.Success(false);
                ChangeStatus(job, JobStatus.Expired, null, "validity_expired");
                await _db.SaveChangesAsync(cancellationToken);
                return Result.Success(true);
            }, cancellationToken);
            if (result.IsSuccess && result.Value) count++;
        }
        return count;
    }

    private DateTimeOffset Now => _clock.UtcNow.ToUniversalTime();
    private static bool Eligible(StudioProfile studio) => studio.DeletedAt is null && studio.SuspendedAt is null && studio.VerificationStatus == StudioVerificationStatus.Verified;
    private static bool ValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 1000;
    private static Error Denied() => Error.Forbidden("Jobs.Forbidden", "This account does not have permission for this studio/job operation.");
    private static Error Missing() => Error.NotFound("Jobs.NotFound", "Job or studio was not found.");
    private static Error Stale() => Error.Conflict("Jobs.StaleVersion", "The job changed. Reload it before trying again.");
    private static Error Invalid(string code, string message) => Error.Validation("Jobs." + code, message);

    private async Task<bool> HasAccessAsync(Guid userId, Guid studioId, bool write, CancellationToken ct) =>
        await _db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.DeletedAt == null && u.AccountType == AccountType.Studio, ct) &&
        await _db.StudioProfiles.AnyAsync(s => s.Id == studioId && s.DeletedAt == null, ct) &&
        await _db.StudioMembers.AnyAsync(m => m.StudioId == studioId && m.UserId == userId &&
            (!write || m.MemberRole == StudioMemberRole.Owner || m.MemberRole == StudioMemberRole.Recruiter), ct);

    private async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct) =>
        await _db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.DeletedAt == null &&
            (u.AccountType == AccountType.Admin || (from ur in _db.UserRoles join r in _db.Roles on ur.RoleId equals r.Id
                where ur.UserId == u.Id && r.NormalizedName == "ADMIN" select ur).Any()), ct);

    private async Task<Job?> LoadLockedJobAsync(Guid jobId, Guid studioId, CancellationToken ct)
    {
        if (_db.Database.IsNpgsql())
        {
            // ADO command avoids projecting xmin incorrectly through raw EF queries.
            await LockAsync("SELECT 1 FROM jobs WHERE id = @id AND studio_id = @studio FOR UPDATE", jobId, studioId, ct);
        }
        var job = await _db.Jobs.SingleOrDefaultAsync(x => x.Id == jobId && x.StudioId == studioId, ct);
        if (job is not null) await _db.Entry(job).ReloadAsync(ct);
        return job;
    }

    private async Task LockAsync(string sql, Guid id, Guid? studio, CancellationToken ct)
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        var idParameter = command.CreateParameter(); idParameter.ParameterName = "id"; idParameter.Value = id; command.Parameters.Add(idParameter);
        if (studio.HasValue)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = "studio"; parameter.Value = studio.Value; command.Parameters.Add(parameter);
        }
        await command.ExecuteScalarAsync(ct);
    }

    private async Task<Result<T>> MutateAsync<T>(Guid studioId, Func<Task<Result<T>>> action, CancellationToken ct)
    {
        // Own the unit of work. Do not allow nesting to obscure transaction/lock ownership.
        if (_db.Database.CurrentTransaction is not null)
            return Result.Failure<T>(Error.Conflict("Jobs.NestedTransaction", "Job lifecycle operations require their own transaction."));
        if (_db.ChangeTracker.HasChanges())
            return Result.Failure<T>(Error.Conflict("Jobs.PendingChanges", "Save or discard other pending changes before a lifecycle operation."));
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            // Consistent lock order: Studio first, Job second. Quotas are studio-wide, across all recruiters/instances.
            if (_db.Database.IsNpgsql()) await LockAsync("SELECT 1 FROM studio_profiles WHERE id = @id FOR UPDATE", studioId, null, ct);
            var trackedStudio = _db.ChangeTracker.Entries<StudioProfile>().SingleOrDefault(x => x.Entity.Id == studioId);
            if (trackedStudio is not null) await trackedStudio.ReloadAsync(ct);
            var result = await action();
            if (result.IsSuccess)
            {
                if (transaction is not null) await transaction.CommitAsync(ct);
                _db.ChangeTracker.Clear();
            }
            else
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
            }
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return Result.Failure<T>(Stale());
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return Result.Failure<T>(Error.Conflict("Jobs.DataConflict", "The job or its reference data changed. Reload and retry with valid data."));
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private void AddHistory(Job job, JobStatus? from, JobStatus to, Guid? userId, string code, string? reason = null) =>
        _db.JobStatusHistory.Add(new JobStatusHistory
        {
            Id = Guid.NewGuid(), JobId = job.Id, PreviousStatus = from, NewStatus = to,
            ActorType = userId.HasValue ? JobHistoryActorType.User : JobHistoryActorType.System,
            ActorUserId = userId, ReasonCode = code, Reason = reason, OccurredAt = Now
        });
    private void ChangeStatus(Job job, JobStatus status, Guid? actor, string code, string? reason = null)
    {
        AddHistory(job, job.Status, status, actor, code, reason);
        job.Status = status;
        job.UpdatedAt = Now;
    }
    private void Audit(Guid actor, string action, string type, Guid id, string reason) =>
        _audit.Enqueue(new AuditLogEntry(action, type, id.ToString(), AuditActorType.Admin, actor, reason.Trim(), RequireReason: true));

    private async Task<JobLifecycleOutcome> RejectedAsync(Job job, string code, string message, CancellationToken ct) =>
        new(false, await SnapshotAsync(job, ct), new[] { new JobPublishFinding(code, JobFindingSeverity.PostingRestriction, null, message) });

    private async Task<Error?> CheckActivationLimitsAsync(Job job, bool publication, DateTimeOffset now, CancellationToken ct)
    {
        var occupied = await _db.Jobs.CountAsync(x => x.StudioId == job.StudioId && x.Id != job.Id &&
            (x.Status == JobStatus.Active || x.Status == JobStatus.Full) && x.ExpiresAt > now, ct);
        if (occupied >= _limits.FreeActiveJobs) return Error.RateLimited("Jobs.ActiveLimit", "Active job limit reached. Existing jobs are unchanged.");
        var others = await _db.Jobs.AsNoTracking().Where(x => x.StudioId == job.StudioId && x.Id != job.Id &&
            (x.Status == JobStatus.Active || x.Status == JobStatus.Paused || x.Status == JobStatus.Full) && x.ExpiresAt > now)
            .Select(x => new { x.NormalizedTitle, x.ContentFingerprint, x.CityId, x.WorkMode }).ToArrayAsync(ct);
        if (others.Any(x => x.ContentFingerprint == job.ContentFingerprint ||
            (x.NormalizedTitle == job.NormalizedTitle && x.CityId == job.CityId && x.WorkMode == job.WorkMode)))
            return Error.Conflict("Jobs.Duplicate", "An equivalent studio listing already exists. Update its openings instead.");
        var cutoff = now.AddDays(-_limits.RepeatCooldownDays);
        if (await _db.JobPublicationEvents.AnyAsync(x => x.StudioId == job.StudioId && x.JobId != job.Id &&
            x.ContentFingerprint == job.ContentFingerprint && x.PublishedAt > cutoff, ct))
            return Error.Conflict("Jobs.RepeatCooldown", "Recently published identical content cannot be reposted yet, even after closing or removal.");
        if (publication && await _db.JobPublicationEvents.CountAsync(x => x.StudioId == job.StudioId && x.PublishedAt > now.AddHours(-24), ct) >= _limits.PublicationsPer24Hours)
            return Error.RateLimited("Jobs.PostingLimit", "Studio publication quota reached. Try again after the rolling window clears.");
        return null;
    }

    private async Task<JobSnapshot> SnapshotAsync(Job job, CancellationToken ct)
    {
        var skills = await _db.JobSkills.AsNoTracking().Where(x => x.JobId == job.Id).OrderBy(x => x.SkillId)
            .Select(x => new JobSkillInput(x.SkillId, x.IsRequired, x.MinProficiency)).ToArrayAsync(ct);
        return new(job.Id, job.StudioId, job.Slug, job.Status, ToContent(job) with { Skills = skills },
            job.ApplicationCap, job.ApplicationsCount, job.FirstPublishedAt, job.PublishedAt, job.ExpiresAt, job.Version);
    }

    private static JobContent ToContent(Job j) => new()
    {
        Title = j.Title, Category = j.Category, JobType = j.JobType, SeniorityMin = j.SeniorityMin, SeniorityMax = j.SeniorityMax,
        MinYears = j.MinYears, MaxYears = j.MaxYears, Description = j.Description, WorkMode = j.WorkMode, CityId = j.CityId,
        RelocationAssistance = j.RelocationAssistance, FresherFriendly = j.FresherFriendly, PayUnit = j.PayUnit,
        SalaryMin = j.SalaryMin, SalaryMax = j.SalaryMax, MaxNoticeDays = j.MaxNoticeDays, Openings = j.Openings, ScreeningQuestion = j.ScreeningQuestion
    };
    private static void ApplyContent(Job j, JobContent c)
    {
        j.Title = c.Title?.Trim(); j.Category = c.Category; j.JobType = c.JobType; j.SeniorityMin = c.SeniorityMin; j.SeniorityMax = c.SeniorityMax;
        j.MinYears = c.MinYears; j.MaxYears = c.MaxYears; j.Description = c.Description?.Trim(); j.WorkMode = c.WorkMode; j.CityId = c.CityId;
        j.RelocationAssistance = c.RelocationAssistance; j.FresherFriendly = c.FresherFriendly; j.PayUnit = c.PayUnit;
        j.SalaryMin = c.SalaryMin; j.SalaryMax = c.SalaryMax; j.MaxNoticeDays = c.MaxNoticeDays; j.Openings = c.Openings; j.ScreeningQuestion = c.ScreeningQuestion?.Trim();
        ApplyFingerprint(j, c);
    }
    private static string Normalize(string? text) => string.Join(" ", (text ?? "").Normalize(NormalizationForm.FormKC)
        .ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static void ApplyFingerprint(Job job, JobContent content)
    {
        var normalized = Normalize(content.Title);
        job.NormalizedTitle = normalized.Length > 100 ? normalized[..100] : normalized;
        // Version 1: identity text only. Changing salary, openings or skill order must not bypass repeat detection.
        var canonical = new { Title = Normalize(content.Title), Description = Normalize(content.Description) };
        job.ContentFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v1:" + JsonSerializer.Serialize(canonical)))).ToLowerInvariant();
    }
    private static bool IsStructurallyComplete(JobContent c) =>
        c.Title?.Trim().Length is >= 5 and <= 100 && c.Description?.Trim().Length is >= 100 and <= 8000 &&
        c.Category.HasValue && c.JobType.HasValue && c.SeniorityMin.HasValue && c.SeniorityMax.HasValue && c.MinYears.HasValue &&
        c.PayUnit.HasValue && c.SalaryMin.HasValue && c.SalaryMax.HasValue && c.WorkMode.HasValue &&
        (c.WorkMode == JobWorkMode.Remote ? c.CityId is null : c.CityId.HasValue);

    private static Error? ValidateDraft(JobContent c)
    {
        if (c.Title?.Length > 100 || c.Description?.Length > 8000 || c.ScreeningQuestion?.Length > 300)
            return Invalid("TextLength", "Title, description or screening question exceeds its maximum length.");
        if (c.MinYears < 0 || c.MaxYears < 0 || c.MinYears > 99.9m || c.MaxYears > 99.9m || c.MinYears > c.MaxYears ||
            c.SalaryMin <= 0 || c.SalaryMax <= 0 || c.SalaryMin > c.SalaryMax || c.SalaryMin > 99999999.99m || c.SalaryMax > 99999999.99m ||
            c.Openings is < 1 or > 50 || c.SeniorityMin > c.SeniorityMax ||
            (c.MaxNoticeDays.HasValue && c.MaxNoticeDays is not (0 or 15 or 30 or 45 or 60 or 90)))
            return Invalid("Ranges", "One or more numeric ranges are invalid.");
        if ((c.Category.HasValue && !Enum.IsDefined(c.Category.Value)) || (c.JobType.HasValue && !Enum.IsDefined(c.JobType.Value)) ||
            (c.SeniorityMin.HasValue && !Enum.IsDefined(c.SeniorityMin.Value)) || (c.SeniorityMax.HasValue && !Enum.IsDefined(c.SeniorityMax.Value)) ||
            (c.WorkMode.HasValue && !Enum.IsDefined(c.WorkMode.Value)) || (c.PayUnit.HasValue && !Enum.IsDefined(c.PayUnit.Value)))
            return Invalid("Enum", "An unknown field option was supplied.");
        if (c.Skills.Count > 12 || c.Skills.Select(x => x.SkillId).Distinct().Count() != c.Skills.Count ||
            c.Skills.Any(x => x.SkillId <= 0 || !Enum.IsDefined(x.MinProficiency)))
            return Invalid("Skills", "Choose up to 12 distinct valid skills with proficiency from 1 to 4.");
        return null;
    }
}
