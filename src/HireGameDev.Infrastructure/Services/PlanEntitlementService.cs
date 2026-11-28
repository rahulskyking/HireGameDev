using System.Data;
using System.Data.Common;
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

// Uncached PostgreSQL reads. Writers acquire the exclusive advisory lock; consumers acquire
// the shared lock before Studio, user, Candidate and Job row locks, held until commit.
// Both default Free and explicit assignments are protected, including missing rows.
internal sealed class PlanEntitlementService(AppDbContext db, IAuditLogger audit) : IPlanEntitlementService
{
    private const long PolicyLock = 487430901671L;
    private static bool Valid(string audience, string plan) =>
        (audience is "candidate" or "studio") && (plan is "free" or "premium");
    private static Error Invalid() => Error.Validation("Plans.Invalid", "Choose a valid plan and provide a reason of at most 500 characters.");

    public Task LockForConsumptionAsync(CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Plan consumption requires a PostgreSQL transaction.");
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({PolicyLock})", cancellationToken);
    }

    public Task<IReadOnlyList<ManualStudioPlan>> GetManualStudioPlansAsync(CancellationToken cancellationToken = default) => WithConnectionAsync<IReadOnlyList<ManualStudioPlan>>(async () => {
        await using var cmd = Command("SELECT studio_id, plan_code FROM manual_studio_plans");
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var items = new List<ManualStudioPlan>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return items;
    }, cancellationToken);

    public Task<bool> PremiumEnabledAsync(CancellationToken cancellationToken = default) => WithConnectionAsync(async () => {
        await using var cmd = Command("SELECT premium_enabled FROM subscription_controls WHERE id = 1");
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is bool enabled ? enabled : throw new InvalidOperationException("Subscription control is missing.");
    }, cancellationToken);

    private async Task<(string Plan, bool Limited)> EffectivePlanAsync(string audience, Guid subject, CancellationToken ct)
    {
        if (!Valid(audience, "free") || subject == Guid.Empty) throw new ArgumentException("Invalid plan target.");
        if (audience == "studio")
        {
            var assigned = await WithConnectionAsync(async () => {
                await using var cmd = Command("SELECT plan_code FROM manual_studio_plans WHERE studio_id = @studio");
                Add(cmd, "studio", subject); return (string?)await cmd.ExecuteScalarAsync(ct);
            }, ct);
            if (assigned is not null)
            {
                if (assigned is not ("plus" or "pro" or "elite")) throw new InvalidOperationException("Invalid Studio plan.");
                return (assigned, true);
            }
        }
        return ("free", await PremiumEnabledAsync(ct));
    }

    public async Task<PlanEntitlement> GetAsync(string audience, Guid profileId, CancellationToken cancellationToken = default)
    {
        var effective = await EffectivePlanAsync(audience, profileId, cancellationToken);
        if (!effective.Limited) return new PlanEntitlement(effective.Plan, null);
        if (effective.Plan != "free")
            return new PlanEntitlement(effective.Plan, await ReadCatalogLimitAsync(audience, effective.Plan, "active_jobs", false, cancellationToken));
        return await WithConnectionAsync(async () => {
            await using var cmd = Command("SELECT quota_limit FROM plan_definitions WHERE audience = @audience AND code = 'free'");
            Add(cmd, "audience", audience);
            var value = await cmd.ExecuteScalarAsync(cancellationToken);
            return value is int limit && limit is >= 1 and <= 10000 ? new PlanEntitlement("free", limit) : throw new InvalidOperationException("Free plan limit is invalid.");
        }, cancellationToken);
    }

    public async Task<int?> GetStudioPostingLimitAsync(Guid studioId, CancellationToken cancellationToken = default) =>
        await EffectiveCatalogLimitAsync("studio", studioId, "postings", false, cancellationToken);
    public async Task<int?> GetStudioSeatLimitAsync(Guid studioId, CancellationToken cancellationToken = default) =>
        await EffectiveCatalogLimitAsync("studio", studioId, "seats", false, cancellationToken);
    public Task<int?> GetCandidateSavedJobLimitAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
        EffectiveCatalogLimitAsync("candidate", candidateId, "saved_jobs", true, cancellationToken);

    public async Task<int?> GetSearchLimitAsync(string audience, Guid subject, CancellationToken cancellationToken = default)
    {
        var effective = await EffectivePlanAsync(audience, subject, cancellationToken);
        if (!effective.Limited) return null;
        if (effective.Plan != "free") return await ReadCatalogLimitAsync(audience, effective.Plan, "searches", false, cancellationToken);
        return await WithConnectionAsync(async () => {
            await using var cmd = Command("SELECT daily_limit FROM search_quota_settings WHERE audience = @audience");
            Add(cmd, "audience", audience); await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Search setting missing.");
            return reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0);
        }, cancellationToken);
    }

    private async Task<int?> EffectiveCatalogLimitAsync(string audience, Guid subject, string feature, bool allowsUnlimited, CancellationToken ct)
    {
        var effective = await EffectivePlanAsync(audience, subject, ct);
        return effective.Limited ? await ReadCatalogLimitAsync(audience, effective.Plan, feature, allowsUnlimited, ct) : null;
    }

    private Task<int?> ReadCatalogLimitAsync(string audience, string plan, string feature, bool allowsUnlimited, CancellationToken ct) => WithConnectionAsync(async () => {
        await using var cmd = Command("SELECT value FROM plan_catalog_values WHERE audience = @audience AND plan_code = @plan AND feature = @feature");
        Add(cmd, "audience", audience); Add(cmd, "plan", plan); Add(cmd, "feature", feature);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Plan limit is missing.");
        var raw = reader.GetString(0);
        if (allowsUnlimited && raw == "unlimited") return (int?)null;
        return int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 10000
            ? number : throw new InvalidOperationException("Plan limit is invalid.");
    }, ct);

    public Task<Result> SetPremiumEnabledAsync(Guid actor, bool enabled, string reason, CancellationToken cancellationToken = default)
    {
        if (actor == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) return Task.FromResult(Result.Failure(Invalid()));
        return MutateAsync(actor, async () => {
            var before = await PremiumEnabledAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE subscription_controls SET premium_enabled = {enabled} WHERE id = 1", cancellationToken);
            audit.Enqueue(new AuditLogEntry("plans.premium_switch_updated", "subscription_controls", "1", AuditActorType.Admin, actor, reason.Trim(), new { Enabled = before }, new { Enabled = enabled }, RequireReason: true));
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PlanLimit>> GetLimitsAsync(CancellationToken cancellationToken = default) => await WithConnectionAsync(async () => {
        await using var cmd = Command("SELECT audience, code, quota_limit FROM plan_definitions WHERE code = 'free' ORDER BY audience");
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var plans = new List<PlanLimit>();
        while (await reader.ReadAsync(cancellationToken)) plans.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        if (plans.Count != 2 || plans.Any(x => !Valid(x.Audience, x.Plan) || x.Plan != "free" || x.Limit is < 1 or > 10000)) throw new InvalidOperationException("Plan definitions are incomplete or invalid.");
        return plans;
    }, cancellationToken);

    public async Task<Result> SetLimitAsync(Guid actor, string audience, string plan, int limit, string? reason, CancellationToken cancellationToken = default)
    {
        if (actor == Guid.Empty || !Valid(audience, plan) || plan != "free" || limit is < 1 or > 10000 || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) return Result.Failure(Invalid());
        return await MutateAsync(actor, async () => {
            var before = (await GetLimitsAsync(cancellationToken)).Single(x => x.Audience == audience && x.Plan == plan).Limit;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE plan_definitions SET quota_limit = {limit}, updated_at = now() WHERE audience = {audience} AND code = {plan}", cancellationToken);
            audit.Enqueue(new AuditLogEntry("plans.limit_updated", "plan_definition", audience + "/" + plan, AuditActorType.Admin, actor, reason.Trim(), new { Limit = before }, new { Limit = limit }, RequireReason: true));
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> AssignAsync(Guid actor, string audience, Guid profileId, string plan, string? reason, CancellationToken cancellationToken = default)
    {
        if (actor == Guid.Empty || audience != "studio" || profileId == Guid.Empty || plan is not ("free" or "plus" or "pro" or "elite") || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Task.FromResult(Result.Failure(Invalid()));
        return MutateAsync(actor, async () => {
            var exists = await db.StudioProfiles.AsNoTracking().AnyAsync(s => s.Id == profileId && s.DeletedAt == null, cancellationToken);
            if (!exists) return Result.Failure(Error.NotFound("Plans.StudioNotFound", "Studio not found."));
            var before = await EffectivePlanAsync("studio", profileId, cancellationToken);
            if (plan == "free") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM manual_studio_plans WHERE studio_id = {profileId}", cancellationToken);
            else await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO manual_studio_plans (studio_id, plan_code) VALUES ({profileId}, {plan}) ON CONFLICT (studio_id) DO UPDATE SET plan_code = EXCLUDED.plan_code, updated_at = now()", cancellationToken);
            audit.Enqueue(new AuditLogEntry("plans.studio_manual_assignment", "studio_profile", profileId.ToString(), AuditActorType.Admin, actor, reason.Trim(), new { before.Plan, before.Limited }, new { Plan = plan, Manual = plan != "free" }, RequireReason: true));
            return Result.Success();
        }, cancellationToken);
    }

    private async Task<Result> MutateAsync(Guid actor, Func<Task<Result>> action, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges()) return Result.Failure(Error.Failure("Plans.Unavailable", "Plan administration requires a clean PostgreSQL transaction."));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({PolicyLock})", ct);
            var admin = await (from u in db.Users.AsNoTracking() join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where u.Id == actor && u.IsActive && u.DeletedAt == null && r.NormalizedName == HireGameDevRoleNames.Admin.ToUpperInvariant() select u.Id).AnyAsync(ct);
            if (!admin) return Result.Failure(Error.Forbidden("Plans.AdminRequired", "An active Admin is required."));
            var result = await action();
            if (result.IsFailure) return result;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return Result.Success();
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    private DbCommand Command(string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
    }
    private async Task<T> WithConnectionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Plan entitlements require PostgreSQL.");
        var opened = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(ct);
        try { return await action(); }
        finally { if (opened) await db.Database.CloseConnectionAsync(); }
    }
}
