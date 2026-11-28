using System.Data;
using System.Globalization;
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

// Only Free Studio postings are connected to live enforcement; the rest remain proposals.
internal sealed class PlanCatalogService(AppDbContext db, IAuditLogger audit) : IPlanCatalogService
{
    private const long PolicyLock = 487430901671L;

    public async Task<IReadOnlyList<PlanCatalogItem>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Plan catalog requires PostgreSQL.");
        var opened = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                SELECT v.audience, v.plan_code, v.feature, f.label, v.value, f.value_type,
                       f.allowed_values, f.allows_unlimited, v.version
                FROM plan_catalog_values v JOIN plan_catalog_features f
                  ON f.audience = v.audience AND f.feature = v.feature
                ORDER BY v.audience, CASE v.plan_code WHEN 'free' THEN 0 WHEN 'plus' THEN 1 WHEN 'pro' THEN 2 WHEN 'elite' THEN 3 ELSE 4 END, f.label
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var items = new List<PlanCatalogItem>();
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetFieldValue<string[]>(6), reader.GetBoolean(7), reader.GetInt64(8)));
            return items;
        }
        finally { if (opened) await db.Database.CloseConnectionAsync(); }
    }

    public async Task<Result> UpdateAsync(Guid actor, string audience, string plan, string feature, string value,
        long version, string reason, CancellationToken cancellationToken = default)
    {
        if (actor == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 ||
            string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(plan) || string.IsNullOrWhiteSpace(feature) ||
            string.IsNullOrWhiteSpace(value) || value.Length > 32 || version < 1)
            return Result.Failure(Error.Validation("Catalog.Invalid", "Choose a valid value and provide a reason."));
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure(Error.Failure("Catalog.Unavailable", "A clean PostgreSQL transaction is required."));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({PolicyLock})", cancellationToken);
            var isAdmin = await (from u in db.Users.AsNoTracking() join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
                join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where u.Id == actor && u.IsActive && u.DeletedAt == null && r.NormalizedName == HireGameDevRoleNames.Admin.ToUpperInvariant()
                select u.Id).AnyAsync(cancellationToken);
            if (!isAdmin) return Result.Failure(Error.Forbidden("Catalog.AdminRequired", "An active Admin is required."));
            var item = (await GetAsync(cancellationToken)).SingleOrDefault(x => x.Audience == audience && x.Plan == plan && x.Feature == feature);
            if (item is null) return Result.Failure(Error.NotFound("Catalog.NotFound", "Plan setting not found."));
            if (item.Version != version) return Result.Failure(Error.Conflict("Catalog.Stale", "This setting changed. Reload and try again."));
            value = value.Trim().ToLowerInvariant();
            var valid = item.ValueType == "number"
                ? (item.AllowsUnlimited && value == "unlimited") || (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 10000)
                : item.ValueType == "choice" && item.AllowedValues.Contains(value, StringComparer.Ordinal);
            if (!valid) return Result.Failure(Error.Validation("Catalog.InvalidValue", "Choose a permitted value."));
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE plan_catalog_values SET value = {value}, version = version + 1 WHERE audience = {audience} AND plan_code = {plan} AND feature = {feature} AND version = {version}", cancellationToken);
            if (changed != 1) return Result.Failure(Error.Conflict("Catalog.Stale", "This setting changed. Reload and try again."));
            audit.Enqueue(new AuditLogEntry("plans.catalog_value_updated", "plan_catalog", $"{audience}/{plan}/{feature}", AuditActorType.Admin,
                actor, reason.Trim(), new { item.Value, item.Version }, new { Value = value, Version = version + 1 }, RequireReason: true));
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return Result.Success();
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }
}
