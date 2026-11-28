using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Abstractions.Applications;

// Live discoverable profile, not an application or submitted snapshot.
public sealed record StudioCandidateRecommendation(string Name, string Headline, ApplicationFitScore Fit);
public sealed record StudioCandidateRecommendationsPage(Guid JobId, Guid StudioId, string JobTitle, IReadOnlyList<StudioCandidateRecommendation> Items, RecommendationPolicy Policy);
public interface IStudioCandidateRecommendationService
{
    Task<Result<StudioCandidateRecommendationsPage>> GetAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default);
}
