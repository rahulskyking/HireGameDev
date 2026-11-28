using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Plans;

// Advisory read; publication rechecks while holding the Studio and policy locks.
public sealed record StudioPostingUsage(int Used, int? Limit, DateTimeOffset? NextAvailableAt);
public interface IStudioPostingUsageService
{
    Task<Result<StudioPostingUsage>> GetAsync(Guid actorUserId, Guid studioId, CancellationToken cancellationToken = default);
}
