using System.Data;
using System.Data.Common;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HireGameDev.Infrastructure.Services;

// PostgreSQL only. Both policy updates and usage transactions participate in the same
// plan advisory lock, then lock the relevant Candidate user/Studio row before checking usage.
internal sealed class SearchQuotaService(AppDbContext db, IClock clock, IPlanEntitlementService plans, IAuditLogger audit) : ISearchQuotaService
{
    private const long PolicyLock = 487430901671L; // Same lock as PlanEntitlementService.
    private static bool ValidAudience(string a) => a is "candidate" or "studio";
    private static bool ValidLimit(int? value) => !value.HasValue || value.Value is >= 1 and <= 10000;
    private static bool ValidFingerprint(string hash) => hash.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static Error Denied() => Error.Forbidden("Search.NotAllowed", "An active account and Studio membership are required.");

    public async Task<SearchQuotaSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => await WithConnection(async () => {
        await using var cmd = Command("SELECT audience, daily_limit FROM search_quota_settings ORDER BY audience");
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        int? candidate = null, studio = null; var seen = new HashSet<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var audience = reader.GetString(0); int? limit = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            if (!ValidAudience(audience) || !ValidLimit(limit) || !seen.Add(audience)) throw new InvalidOperationException("Invalid search quota settings.");
            if (audience == "candidate") candidate = limit; else studio = limit;
        }
        if (seen.Count != 2) throw new InvalidOperationException("Search quota settings are incomplete.");
        return new SearchQuotaSettings(candidate, studio);
    }, cancellationToken);

    public async Task<Result> UpdateSettingsAsync(Guid admin, SearchQuotaSettings settings, string reason, CancellationToken ct = default)
    {
        if (admin == Guid.Empty || settings is null || !ValidLimit(settings.CandidateDaily) || !ValidLimit(settings.StudioDaily) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result.Failure(Error.Validation("Search.InvalidSettings", "Enter limits from 1 to 10000 or leave them blank, and provide a reason."));
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) return Result.Failure(Error.Failure("Search.Unavailable", "Search settings require a clean PostgreSQL transaction."));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({PolicyLock})", ct);
            var isAdmin = await (from u in db.Users.AsNoTracking() join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where u.Id == admin && u.IsActive && u.DeletedAt == null && r.NormalizedName == HireGameDevRoleNames.Admin.ToUpperInvariant() select u.Id).AnyAsync(ct);
            if (!isAdmin) return Result.Failure(Denied());
            var before = await GetSettingsAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE search_quota_settings SET daily_limit = {settings.CandidateDaily} WHERE audience = 'candidate'", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE search_quota_settings SET daily_limit = {settings.StudioDaily} WHERE audience = 'studio'", ct);
            audit.Enqueue(new AuditLogEntry("search.limits_updated", "search_quota", "free", AuditActorType.Admin, admin, reason.Trim(), before, settings, RequireReason: true));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear(); return Result.Success();
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    public async Task<Result<SearchQuotaCheck>> CheckAsync(Guid actor, string audience, Guid? studioId, string fingerprint, Guid? runId, CancellationToken ct = default)
    {
        if (actor == Guid.Empty || !ValidAudience(audience) || !ValidFingerprint(fingerprint)) return Result.Failure<SearchQuotaCheck>(Denied());
        var subject = await ResolveSubject(actor, audience, studioId, ct);
        if (!subject.HasValue) return Result.Failure<SearchQuotaCheck>(Denied());
        var limit = await plans.GetSearchLimitAsync(audience, subject.Value, ct);
        if (!limit.HasValue) return Result.Success(new SearchQuotaCheck(false, true, 0, null, null));
        return Result.Success(await ReadUsage(actor, audience, subject.Value, fingerprint, runId, limit.Value, clock.UtcNow.ToUniversalTime(), ct));
    }

    public async Task<Result<SearchQuotaStart>> StartAsync(Guid actor, string audience, Guid? studioId, string fingerprint, Guid? priorRun, Guid intentId, CancellationToken ct = default)
    {
        if (actor == Guid.Empty || intentId == Guid.Empty || !ValidAudience(audience) || !ValidFingerprint(fingerprint) || !db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure<SearchQuotaStart>(Denied());
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await plans.LockForConsumptionAsync(ct);
            var subject = await ResolveSubject(actor, audience, studioId, ct);
            if (!subject.HasValue) return Result.Failure<SearchQuotaStart>(Denied());
            if (audience == "candidate") await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {subject.Value} FOR UPDATE", ct);
            else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM studio_profiles WHERE id = {subject.Value} FOR UPDATE", ct);
            // Membership/account might have changed while waiting for the subject lock.
            if (await ResolveSubject(actor, audience, studioId, ct) != subject) return Result.Failure<SearchQuotaStart>(Denied());
            var limit = await plans.GetSearchLimitAsync(audience, subject.Value, ct);
            if (!limit.HasValue) { await tx.CommitAsync(ct); return Result.Success(new SearchQuotaStart(null, new(false, true, 0, null, null))); }
            var now = clock.UtcNow.ToUniversalTime();
            var duplicate = await FindIntent(actor, audience, subject.Value, fingerprint, intentId, now, ct);
            if (duplicate.Exists)
            {
                if (!duplicate.Match) return Result.Failure<SearchQuotaStart>(Error.Conflict("Search.IntentUsed", "This search form has already been used. Reload and retry."));
                var previousUsage = await ReadUsage(actor, audience, subject.Value, fingerprint, duplicate.Run, limit.Value, now, ct);
                if (!previousUsage.AuthorizedRun) return Result.Failure<SearchQuotaStart>(Error.Conflict("Search.IntentExpired", "Your current search changed. Reload and retry."));
                await tx.CommitAsync(ct); return Result.Success(new SearchQuotaStart(duplicate.Run, previousUsage));
            }
            var usage = await ReadUsage(actor, audience, subject.Value, fingerprint, priorRun, limit.Value, now, ct);
            if (usage.AuthorizedRun) { await tx.CommitAsync(ct); return Result.Success(new SearchQuotaStart(priorRun, usage)); }
            if (usage.Used >= limit.Value) return Result.Failure<SearchQuotaStart>(Error.RateLimited("Search.DailyLimit", usage.NextAvailableAt.HasValue ? $"Daily search limit reached. Next search becomes available at {usage.NextAvailableAt:u}." : "Search allowance is currently zero."));
            var run = Guid.NewGuid();
            if (audience == "candidate")
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO search_usage_events (run_id, intent_id, audience, candidate_user_id, actor_id, fingerprint, occurred_at) VALUES ({run}, {intentId}, {audience}, {subject.Value}, {actor}, {fingerprint}, {now})", ct);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO search_usage_events (run_id, intent_id, audience, studio_id, actor_id, fingerprint, occurred_at) VALUES ({run}, {intentId}, {audience}, {subject.Value}, {actor}, {fingerprint}, {now})", ct);
            // Expired records for this account are no longer needed for rolling quota or run validation.
            if (audience == "candidate") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM search_usage_events WHERE candidate_user_id = {subject.Value} AND occurred_at <= {now.AddDays(-1)}", ct);
            else await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM search_usage_events WHERE studio_id = {subject.Value} AND occurred_at <= {now.AddDays(-1)}", ct);
            await tx.CommitAsync(ct);
            return Result.Success(new SearchQuotaStart(run, new(true, true, usage.Used + 1, limit, null)));
        }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<Guid?> ResolveSubject(Guid actor, string audience, Guid? studioId, CancellationToken ct)
    {
        if (audience == "candidate") return await db.Users.AsNoTracking().Where(u => u.Id == actor && u.IsActive && u.DeletedAt == null && u.AccountType == AccountType.Candidate)
            .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);
        if (!studioId.HasValue || studioId == Guid.Empty) return null;
        return await (from s in db.StudioProfiles.AsNoTracking() join m in db.StudioMembers.AsNoTracking() on s.Id equals m.StudioId join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            where s.Id == studioId && u.Id == actor && u.IsActive && u.DeletedAt == null && u.AccountType == AccountType.Studio && s.DeletedAt == null && s.SuspendedAt == null && s.VerificationStatus == StudioVerificationStatus.Verified &&
                (m.MemberRole == StudioMemberRole.Owner || m.MemberRole == StudioMemberRole.Recruiter || m.MemberRole == StudioMemberRole.Viewer) select (Guid?)s.Id).SingleOrDefaultAsync(ct);
    }

    private async Task<(bool Exists, bool Match, Guid Run)> FindIntent(Guid actor, string audience, Guid subject, string hash, Guid intentId, DateTimeOffset now, CancellationToken ct) => await WithConnection(async () => {
        var column = audience == "candidate" ? "candidate_user_id" : "studio_id";
        await using var cmd = Command($"SELECT run_id, audience = @audience AND {column} = @subject AND fingerprint = @hash AND occurred_at > @since AND occurred_at <= @now FROM search_usage_events WHERE actor_id = @actor AND intent_id = @intent");
        Add(cmd, "actor", actor); Add(cmd, "intent", intentId); Add(cmd, "audience", audience); Add(cmd, "subject", subject); Add(cmd, "hash", hash); Add(cmd, "since", now.AddDays(-1)); Add(cmd, "now", now);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (false, false, Guid.Empty);
        return (true, !reader.IsDBNull(1) && reader.GetBoolean(1), reader.GetGuid(0));
    }, ct);

    private async Task<SearchQuotaCheck> ReadUsage(Guid actor, string audience, Guid subject, string hash, Guid? run, int limit, DateTimeOffset now, CancellationToken ct) => await WithConnection(async () => {
        var column = audience == "candidate" ? "candidate_user_id" : "studio_id"; // fixed allowlist, never user input
        await using var cmd = Command($"""
            SELECT count(*), min(occurred_at), COALESCE((
              SELECT run_id = @run AND fingerprint = @hash FROM search_usage_events
              WHERE audience = @audience AND {column} = @subject AND actor_id = @actor AND occurred_at > @since AND occurred_at <= @now
              ORDER BY sequence DESC LIMIT 1), false)
            FROM search_usage_events WHERE audience = @audience AND {column} = @subject AND occurred_at > @since AND occurred_at <= @now
            """);
        Add(cmd, "audience", audience); Add(cmd, "subject", subject); Add(cmd, "actor", actor); Add(cmd, "hash", hash); Add(cmd, "run", run ?? Guid.Empty);
        Add(cmd, "since", now.AddDays(-1)); Add(cmd, "now", now);
        await using var reader = await cmd.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
        var count = checked((int)reader.GetInt64(0));
        // After a limit reduction, more than one event may need to expire; obtain that time separately below.
        var authorized = reader.GetBoolean(2); await reader.DisposeAsync();
        DateTimeOffset? next = null;
        if (limit > 0 && count >= limit)
        {
            await using var nextCmd = Command($"SELECT occurred_at FROM search_usage_events WHERE audience = @audience AND {column} = @subject AND occurred_at > @since AND occurred_at <= @now ORDER BY occurred_at, sequence OFFSET @offset LIMIT 1");
            Add(nextCmd, "audience", audience); Add(nextCmd, "subject", subject); Add(nextCmd, "since", now.AddDays(-1)); Add(nextCmd, "now", now); Add(nextCmd, "offset", count - limit);
            await using var nextReader = await nextCmd.ExecuteReaderAsync(ct);
            if (await nextReader.ReadAsync(ct)) next = nextReader.GetFieldValue<DateTimeOffset>(0).AddDays(1);
        }
        return new SearchQuotaCheck(true, authorized, count, limit, next);
    }, ct);

    private DbCommand Command(string sql) { var cmd = db.Database.GetDbConnection().CreateCommand(); cmd.CommandText = sql; cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction(); return cmd; }
    private static void Add(DbCommand cmd, string name, object value) { var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value; cmd.Parameters.Add(p); }
    private async Task<T> WithConnection<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Search quotas require PostgreSQL.");
        var opened = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(ct);
        try { return await action(); } finally { if (opened) await db.Database.CloseConnectionAsync(); }
    }
}
