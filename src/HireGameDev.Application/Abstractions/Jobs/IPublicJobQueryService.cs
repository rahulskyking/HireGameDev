using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;

namespace HireGameDev.Application.Abstractions.Jobs;

/// <summary>Public-safe, read-only catalog. No membership, application or engagement mutations.</summary>
public interface IPublicJobQueryService
{
    // viewerUserId must come from trusted authentication context, never query/form binding.
    // Service rechecks primary Candidate identity before applying personal blocklist preferences.
    Task<Result<PublicJobSearchPage>> SearchAsync(PublicJobSearch search, CancellationToken cancellationToken = default, Guid? viewerUserId = null);
    // Hidden and missing listings deliberately return the same neutral unavailable result.
    Task<Result<PublicJobDetail>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default, Guid? viewerUserId = null);
}
