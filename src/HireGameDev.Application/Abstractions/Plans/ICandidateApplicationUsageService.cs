using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Plans;

// Development workflow only. Counts successful submissions, not draft reviews or withdrawals.
public sealed class ApplicationQuotaOptions
{
    public const string SectionName = "ApplicationQuota";
    public int Limit { get; set; } = 10;
    public int WindowDays { get; set; } = 30;
}

public sealed record CandidateApplicationUsage(int Used, int? Limit, int WindowDays, DateTimeOffset? NextAvailableAt, string? Plan = null)
{
    public bool CanSubmit => !Limit.HasValue || Used < Limit.Value;
}

public interface ICandidateApplicationUsageService
{
    Task<Result<CandidateApplicationUsage>> GetAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}
