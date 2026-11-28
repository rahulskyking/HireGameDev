using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

internal sealed class StudioPostingUsageService(AppDbContext db, IClock clock, IPlanEntitlementService plans) : IStudioPostingUsageService
{
    public async Task<Result<StudioPostingUsage>> GetAsync(Guid actorUserId, Guid studioId, CancellationToken cancellationToken = default)
    {
        var authorized = actorUserId != Guid.Empty && studioId != Guid.Empty && await (
            from member in db.StudioMembers.AsNoTracking()
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id
            join studio in db.StudioProfiles.AsNoTracking() on member.StudioId equals studio.Id
            where member.StudioId == studioId && member.UserId == actorUserId && user.IsActive && user.DeletedAt == null &&
                user.AccountType == AccountType.Studio && studio.DeletedAt == null &&
                (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer)
            select member.UserId).AnyAsync(cancellationToken);
        if (!authorized) return Result.Failure<StudioPostingUsage>(Error.Forbidden("Jobs.UsageForbidden", "Studio membership is required."));
        var limit = await plans.GetStudioPostingLimitAsync(studioId, cancellationToken);
        return Result.Success(await StudioPostingQuota.ReadAsync(db, studioId, limit, clock.UtcNow.ToUniversalTime(), cancellationToken));
    }
}

internal static class StudioPostingQuota
{
    internal static async Task<StudioPostingUsage> ReadAsync(AppDbContext db, Guid studioId, int? limit, DateTimeOffset now, CancellationToken ct)
    {
        if (limit.HasValue && limit.Value < 0) throw new InvalidOperationException("Invalid posting quota.");
        var since = now.AddDays(-30);
        var query = db.JobPublicationEvents.AsNoTracking().Where(x => x.StudioId == studioId && x.PublishedAt > since && x.PublishedAt <= now);
        var count = await query.CountAsync(ct);
        DateTimeOffset? next = null;
        if (limit.HasValue && limit.Value > 0 && count >= limit.Value)
        {
            var release = await query.OrderBy(x => x.PublishedAt).ThenBy(x => x.Id)
                .Skip(count - limit!.Value).Select(x => (DateTimeOffset?)x.PublishedAt).FirstOrDefaultAsync(ct);
            next = release?.AddDays(30);
        }
        return new StudioPostingUsage(count, limit, next);
    }
}
