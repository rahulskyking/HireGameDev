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
    private const string Key = "recommendations.candidate_job_policy";
    public async Task<Result<RecommendationPolicy>> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await settings.GetStringAsync(Key, JsonSerializer.Serialize(RecommendationPolicy.Default), cancellationToken);
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

    public async Task<Result> UpdateAsync(Guid actorUserId, RecommendationPolicy policy, string reason, CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || policy is null || !policy.IsValid || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result.Failure(Error.Validation("Recommendations.InvalidPolicy", "Choose percentages from 0 to 100 and provide a reason."));
        var result = await settings.UpdateSettingAsync(new UpdateAppSettingCommand(Key, JsonSerializer.Serialize(policy), actorUserId, reason.Trim(),
            "Candidate job recommendation thresholds; not match weights or authorization."), cancellationToken);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
