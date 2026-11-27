using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Application.Features.Jobs;

namespace HireGameDev.Application.Abstractions.Applications;

public sealed record CandidateJobRecommendation(PublicJobCard Job, ApplicationFitScore Fit);
public sealed record CandidateJobRecommendationsPage(IReadOnlyList<CandidateJobRecommendation> Items, RecommendationPolicy Policy);

// The actor is always the authenticated Candidate, never a client-selected profile.
public interface ICandidateJobRecommendationService
{
    Task<Result<CandidateJobRecommendationsPage>> GetAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}
