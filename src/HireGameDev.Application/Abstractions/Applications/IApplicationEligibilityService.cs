using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Abstractions.Applications;

public interface IApplicationEligibilityService
{
    // actorUserId must be derived from trusted authentication, never from request data.
    // Advisory only: not a capacity reservation or permission to skip transactional revalidation.
    Task<Result<ApplicationEligibility>> CheckAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default);
}
