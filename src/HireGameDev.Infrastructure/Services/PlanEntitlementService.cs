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

    public async Task<PlanEntitlement> GetAsync(string audience, Guid profileId, CancellationToken cancellationToken = default)
    {
        if (!Valid(audience, "free") || profileId == Guid.Empty) throw new ArgumentException("Invalid plan target.");
        // Free-only initial release: retain historical assignments but do not activate them.
        const string sql = "SELECT p.code, p.quota_limit FROM plan_definitions p WHERE p.audience = @audience AND p.code = 'free'";
        return await WithConnectionAsync(async () => {
            await using var cmd = Command(sql); Add(cmd, "audience", audience);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Plan definition is missing.");
            var result = new PlanEntitlement(reader.GetString(0), reader.GetInt32(1));
            if (result.Limit is < 1 or > 10000 || await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Invalid plan definition.");
            return result;
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

    // Assignments are intentionally disabled until paid plans are explicitly launched.
    // Existing database rows are retained, but GetAsync always enforces Free for now.
    public Task<Result> AssignAsync(Guid actor, string audience, Guid profileId, string plan, string? reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(Error.Forbidden("Plans.AssignmentsDisabled", "Plan assignments are not available in the Free release.")));

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
