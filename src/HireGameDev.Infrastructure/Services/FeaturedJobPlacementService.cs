using System.Data;
using System.Data.Common;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HireGameDev.Infrastructure.Services;

// A single labelled slot, only on the default first browse page. It never participates in
// match scoring, organic ordering, totals or pagination. No billing is implied.
internal sealed class FeaturedJobPlacementService(AppDbContext db, IClock clock, IPlanEntitlementService plans,
    IPublicJobQueryService jobs, IAuditLogger audit, ILogger<FeaturedJobPlacementService> logger) : IFeaturedJobPlacementService
{
    public async Task<PublicJobCard?> GetForViewerAsync(Guid? viewerUserId, CancellationToken cancellationToken = default)
    {
        try { return await GetEligibleAsync(viewerUserId, cancellationToken); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Optional placement must never take down organic browsing during migration rollout.
            logger.LogWarning(ex, "Featured placements unavailable: schema is not ready.");
            return null;
        }
    }

    private async Task<PublicJobCard?> GetEligibleAsync(Guid? viewerUserId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.ToUniversalTime();
        // Read a bounded pool of currently eligible Premium placements; the public catalog
        // then independently rechecks job visibility and the Candidate blocklist.
        var rows = await WithConnectionAsync(async () => {
            await using var cmd = Command("""
                SELECT candidates.id, candidates.slug, candidates.studio_id FROM (
                SELECT j.id, j.slug, j.studio_id FROM featured_job_placements f
                JOIN jobs j ON j.id = f.job_id
                JOIN studio_profiles s ON s.id = j.studio_id
                JOIN studio_plan_assignments a ON a.studio_id = s.id AND a.audience = 'studio' AND a.plan_code = 'premium'
                WHERE f.starts_at <= @now AND f.ends_at > @now AND j.status = 'active' AND j.expires_at > @now
                  AND j.applications_count < j.application_cap AND s.verification_status = 'verified'
                  AND s.deleted_at IS NULL AND s.suspended_at IS NULL
                ORDER BY f.ends_at, j.id LIMIT 25
                ) candidates ORDER BY md5(candidates.id::text || @rotation), candidates.id
                """);
            Add(cmd, "now", now); Add(cmd, "rotation", now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            var result = new List<(Guid Id, string Slug, Guid StudioId)>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add((reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2)));
            return result;
        }, cancellationToken);
        foreach (var row in rows)
        {
            var visible = await jobs.GetBySlugAsync(row.Slug, cancellationToken, viewerUserId);
            if (visible.IsFailure || visible.Value.Summary.Id != row.Id || visible.Value.Summary.Studio.Id != row.StudioId) continue;
            // No cache: a downgrade suppresses placement on the next read. In-flight responses
            // can still race any change, like other public discovery responses.
            if ((await plans.GetAsync("studio", row.StudioId, cancellationToken)).Plan != "premium") continue;
            return visible.Value.Summary;
        }
        return null;
    }

    public async Task<IReadOnlyList<FeaturedJobAdminItem>> ListAsync(CancellationToken cancellationToken = default) => await WithConnectionAsync(async () => {
        await using var cmd = Command("""
            SELECT j.id, j.title, s.name, f.starts_at, f.ends_at FROM featured_job_placements f
            JOIN jobs j ON j.id = f.job_id JOIN studio_profiles s ON s.id = j.studio_id
            ORDER BY f.ends_at DESC, j.id LIMIT 100
            """);
        var result = new List<FeaturedJobAdminItem>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? "Untitled job" : reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4)));
        return result;
    }, cancellationToken);

    public async Task<Result> SetAsync(Guid actorUserId, Guid jobId, bool enabled, int days, string? reason, CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || jobId == Guid.Empty || (enabled && (days < 1 || days > 30)) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result.Failure(Error.Validation("Placement.Invalid", "Choose a job, a duration from 1 to 30 days, and provide a reason."));
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure(Error.Failure("Placement.Unavailable", "Placement changes require a clean PostgreSQL transaction."));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Shared entitlement lock precedes the Studio then Job locks, matching lifecycle.
            await plans.LockForConsumptionAsync(cancellationToken);
            var admin = await (from u in db.Users.AsNoTracking() join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
                join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where u.Id == actorUserId && u.IsActive && u.DeletedAt == null && r.NormalizedName == HireGameDevRoleNames.Admin.ToUpperInvariant()
                select u.Id).AnyAsync(cancellationToken);
            if (!admin) return Result.Failure(Error.Forbidden("Placement.AdminRequired", "An active Admin is required."));
            var studioId = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => (Guid?)j.StudioId).SingleOrDefaultAsync(cancellationToken);
            if (!studioId.HasValue) return Result.Failure(Error.NotFound("Placement.NotFound", "Job not found."));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM studio_profiles WHERE id = {studioId.Value} FOR UPDATE", cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM jobs WHERE id = {jobId} AND studio_id = {studioId.Value} FOR UPDATE", cancellationToken);
            var job = await (from j in db.Jobs.AsNoTracking() join s in db.StudioProfiles.AsNoTracking() on j.StudioId equals s.Id
                where j.Id == jobId && s.Id == studioId.Value
                select new { j.Status, j.ExpiresAt, j.ApplicationsCount, j.ApplicationCap, s.VerificationStatus, s.DeletedAt, s.SuspendedAt }).SingleOrDefaultAsync(cancellationToken);
            if (job is null) return Result.Failure(Error.NotFound("Placement.NotFound", "Job not found."));
            var now = clock.UtcNow.ToUniversalTime();
            if (enabled && (job.Status != JobStatus.Active || !job.ExpiresAt.HasValue || job.ExpiresAt <= now || job.ApplicationsCount >= job.ApplicationCap ||
                job.VerificationStatus != StudioVerificationStatus.Verified || job.DeletedAt != null || job.SuspendedAt != null ||
                (await plans.GetAsync("studio", studioId.Value, cancellationToken)).Plan != "premium"))
                return Result.Failure(Error.Validation("Placement.Ineligible", "An active, unexpired job with capacity from a verified, unsuspended Premium Studio is required."));
            var before = await ReadEndAsync(jobId, cancellationToken);
            DateTimeOffset? ends = null;
            if (enabled)
            {
                ends = now.AddDays(days);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO featured_job_placements (job_id, starts_at, ends_at) VALUES ({jobId}, {now}, {ends.Value})
                    ON CONFLICT (job_id) DO UPDATE SET starts_at = EXCLUDED.starts_at, ends_at = EXCLUDED.ends_at
                    """, cancellationToken);
            }
            else await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM featured_job_placements WHERE job_id = {jobId}", cancellationToken);
            audit.Enqueue(new AuditLogEntry("placement.updated", "featured_job", jobId.ToString(), AuditActorType.Admin, actorUserId, reason.Trim(),
                new { EndsAt = before }, new { Enabled = enabled, EndsAt = ends }, RequireReason: true));
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return Result.Success();
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    private async Task<DateTimeOffset?> ReadEndAsync(Guid jobId, CancellationToken ct) => await WithConnectionAsync(async () => {
        await using var cmd = Command("SELECT ends_at FROM featured_job_placements WHERE job_id = @id"); Add(cmd, "id", jobId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetFieldValue<DateTimeOffset>(0).ToUniversalTime() : (DateTimeOffset?)null;
    }, ct);
    private DbCommand Command(string sql) { var cmd = db.Database.GetDbConnection().CreateCommand(); cmd.CommandText = sql; cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction(); return cmd; }
    private static void Add(DbCommand cmd, string name, object value) { var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value; cmd.Parameters.Add(p); }
    private async Task<T> WithConnectionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Featured placements require PostgreSQL.");
        var opened = db.Database.GetDbConnection().State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(ct);
        try { return await action(); } finally { if (opened) await db.Database.CloseConnectionAsync(); }
    }
}
