using System.Text.Json;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Settings;

namespace HireGameDev.Infrastructure.Services;

// The settings service writes the policy and its audit entry together. Thresholds are not
// authorization or quota checks; the existing 60-second settings cache can delay propagation.
internal sealed class RecommendationPolicyService(ISettingsService settings) : IRecommendationPolicyService
{
    private const string CandidateKey = "recommendations.candidate_job_policy";
    private const string StudioKey = "recommendations.studio_candidate_policy";
    public Task<Result<RecommendationPolicy>> GetAsync(CancellationToken cancellationToken = default) => ReadAsync(CandidateKey, cancellationToken);
    public Task<Result<RecommendationPolicy>> GetStudioAsync(CancellationToken cancellationToken = default) => ReadAsync(StudioKey, cancellationToken);

    private async Task<Result<RecommendationPolicy>> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var item = (await settings.GetAllAsync(cancellationToken)).FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item is null) return Result.Success(RecommendationPolicy.Default);
        var raw = item.RawJsonValue;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(nameof(RecommendationPolicy.MinimumScore), out var score) || score.ValueKind != JsonValueKind.Number || !score.TryGetInt32(out var minimumScore) ||
                !root.TryGetProperty(nameof(RecommendationPolicy.MinimumCoverage), out var coverage) || coverage.ValueKind != JsonValueKind.Number || !coverage.TryGetInt32(out var minimumCoverage))
                return Result.Failure<RecommendationPolicy>(Error.Failure("Recommendations.InvalidPolicy", "Recommendation policy is invalid."));
            var policy = new RecommendationPolicy(minimumScore, minimumCoverage);
            return policy.IsValid ? Result.Success(policy)
                : Result.Failure<RecommendationPolicy>(Error.Failure("Recommendations.InvalidPolicy", "Recommendation policy is invalid."));
        }
        catch (JsonException) { return Result.Failure<RecommendationPolicy>(Error.Failure("Recommendations.InvalidPolicy", "Recommendation policy is invalid.")); }
    }

    public Task<Result> UpdateAsync(Guid actorUserId, RecommendationPolicy policy, string reason, CancellationToken cancellationToken = default) =>
        WriteAsync(CandidateKey, actorUserId, policy, reason, cancellationToken);
    public Task<Result> UpdateStudioAsync(Guid actorUserId, RecommendationPolicy policy, string reason, CancellationToken cancellationToken = default) =>
        WriteAsync(StudioKey, actorUserId, policy, reason, cancellationToken);

    private async Task<Result> WriteAsync(string key, Guid actorUserId, RecommendationPolicy policy, string reason, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || policy is null || !policy.IsValid || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result.Failure(Error.Validation("Recommendations.InvalidPolicy", "Choose percentages from 0 to 100 and provide a reason."));
        var result = await settings.UpdateSettingAsync(new UpdateAppSettingCommand(key, JsonSerializer.Serialize(policy), actorUserId, reason.Trim(),
            "Recommendation thresholds; not match weights or authorization."), cancellationToken);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
