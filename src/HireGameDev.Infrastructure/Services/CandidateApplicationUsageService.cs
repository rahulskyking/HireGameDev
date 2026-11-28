using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.Services;

// Read is advisory for the UI. Submission calls the same calculation while holding the
// per-candidate lock and must commit its application before releasing that lock.
internal sealed class CandidateApplicationUsageService(AppDbContext db, IClock clock, IOptions<ApplicationQuotaOptions> options, IPlanEntitlementService? plans = null) : ICandidateApplicationUsageService
{
    public async Task<Result<CandidateApplicationUsage>> GetAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var candidate = await (from c in db.CandidateProfiles.AsNoTracking()
            join u in db.Users.AsNoTracking() on c.UserId equals u.Id
            where actorUserId != Guid.Empty && u.Id == actorUserId && u.IsActive && u.DeletedAt == null &&
                u.AccountType == AccountType.Candidate && c.DeletedAt == null
            select (Guid?)c.Id).SingleOrDefaultAsync(cancellationToken);
        if (!candidate.HasValue) return Result.Failure<CandidateApplicationUsage>(Error.Forbidden("Applications.CandidateRequired", "An active Candidate account is required."));
        var entitlement = plans is null ? null : await plans.GetAsync("candidate", candidate.Value, cancellationToken);
        var limit = entitlement is null ? options.Value.Limit : entitlement.Limit;
        var window = entitlement is null ? options.Value.WindowDays : 30;
        var usage = await CandidateApplicationQuota.ReadAsync(db, candidate.Value, clock.UtcNow.ToUniversalTime(), limit, window, cancellationToken);
        return Result.Success(usage with { Plan = entitlement?.Plan });
    }
}

internal static class CandidateApplicationQuota
{
    internal static async Task<CandidateApplicationUsage> ReadAsync(AppDbContext db, Guid candidateId, DateTimeOffset now, int? limit, int windowDays, CancellationToken ct)
    {
        if ((limit.HasValue && limit.Value < 1) || windowDays is < 1 or > 365)
            throw new InvalidOperationException("Invalid application quota configuration.");
        var since = now.AddDays(-windowDays);
        var query = db.Applications.AsNoTracking().Where(a => a.CandidateId == candidateId && a.SubmittedAt > since && a.SubmittedAt <= now);
        var count = await query.CountAsync(ct);
        DateTimeOffset? next = null;
        if (limit.HasValue && count >= limit.Value)
        {
            // Also handles a future plan downgrade where usage exceeds the new limit.
            var oldestReleasingSlot = await query.OrderBy(a => a.SubmittedAt).ThenBy(a => a.Id)
                .Skip(count - limit!.Value).Select(a => (DateTimeOffset?)a.SubmittedAt).FirstOrDefaultAsync(ct);
            next = oldestReleasingSlot?.AddDays(windowDays);
        }
        return new CandidateApplicationUsage(count, limit, windowDays, next);
    }
}
