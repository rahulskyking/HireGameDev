using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Applications;

// Recommendation thresholds only: this does not change match weights or authorization.
public sealed record RecommendationPolicy(int MinimumScore, int MinimumCoverage)
{
    public static RecommendationPolicy Default { get; } = new(60, 70);
    public bool IsValid => MinimumScore is >= 0 and <= 100 && MinimumCoverage is >= 0 and <= 100;
}
public interface IRecommendationPolicyService
{
    Task<Result<RecommendationPolicy>> GetAsync(CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(Guid actorUserId, RecommendationPolicy policy, string reason, CancellationToken cancellationToken = default);
}
