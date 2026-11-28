using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;

namespace HireGameDev.Application.Abstractions.Jobs;

public sealed record FeaturedJobAdminItem(Guid JobId, string Title, string StudioName, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

// Development only: a placement is distinct from matching, organic ranking and billing.
public interface IFeaturedJobPlacementService
{
    Task<PublicJobCard?> GetForViewerAsync(Guid? viewerUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeaturedJobAdminItem>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result> SetAsync(Guid actorUserId, Guid jobId, bool enabled, int days, string? reason, CancellationToken cancellationToken = default);
}
