using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Abstractions.Applications;

// Recommendation thresholds only: this does not change match weights or authorization.
public sealed record RecommendationPolicy(int MinimumScore, int MinimumCoverage)
{
    public static RecommendationPolicy Default { get; } = new(60, 70);
    public bool IsValid => MinimumScore is >= 0 and <= 100 && MinimumCoverage is >= 0 and <= 100;

    // A threshold never replaces the requirement for an explicitly matching listed skill.
    public bool Allows(ApplicationFitScore? fit) => IsValid && fit?.Percentage is int score &&
        score >= MinimumScore && fit.Coverage >= MinimumCoverage && fit.Criteria.Any(c =>
            (c.Label.StartsWith("Required: ", StringComparison.Ordinal) || c.Label.StartsWith("Preferred: ", StringComparison.Ordinal)) && c.Fraction == 1m);
}
public interface IRecommendationPolicyService
{
    Task<Result<RecommendationPolicy>> GetAsync(CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(Guid actorUserId, RecommendationPolicy policy, string reason, CancellationToken cancellationToken = default);
    Task<Result<RecommendationPolicy>> GetStudioAsync(CancellationToken cancellationToken = default);
    Task<Result> UpdateStudioAsync(Guid actorUserId, RecommendationPolicy policy, string reason, CancellationToken cancellationToken = default);
}
