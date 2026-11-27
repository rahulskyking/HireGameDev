using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Application.Features.Jobs;

namespace HireGameDev.Application.Abstractions.Applications;

public sealed record CandidateJobRecommendation(PublicJobCard Job, ApplicationFitScore Fit);

// The actor is always the authenticated Candidate, never a client-selected profile.
public interface ICandidateJobRecommendationService
{
    Task<Result<IReadOnlyList<CandidateJobRecommendation>>> GetAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}
