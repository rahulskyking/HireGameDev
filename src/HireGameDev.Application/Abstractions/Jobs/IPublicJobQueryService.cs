using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;

namespace HireGameDev.Application.Abstractions.Jobs;

/// <summary>Public-safe, read-only catalog. No membership, application or engagement mutations.</summary>
public interface IPublicJobQueryService
{
    Task<Result<PublicJobSearchPage>> SearchAsync(PublicJobSearch search, CancellationToken cancellationToken = default);
    // Hidden and missing listings deliberately return the same neutral unavailable result.
    Task<Result<PublicJobDetail>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}
