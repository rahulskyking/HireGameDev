using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Plans;

public sealed record SearchQuotaSettings(int? CandidateDaily, int? StudioDaily);
public sealed record SearchQuotaCheck(bool Enabled, bool AuthorizedRun, int Used, int? Limit, DateTimeOffset? NextAvailableAt);
public sealed record SearchQuotaStart(Guid? RunId, SearchQuotaCheck Usage);
public interface ISearchQuotaService
{
    Task<SearchQuotaSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<Result> UpdateSettingsAsync(Guid admin, SearchQuotaSettings settings, string reason, CancellationToken cancellationToken = default);
    Task<Result<SearchQuotaCheck>> CheckAsync(Guid actor, string audience, Guid? studioId, string fingerprint, Guid? runId, CancellationToken cancellationToken = default);
    Task<Result<SearchQuotaStart>> StartAsync(Guid actor, string audience, Guid? studioId, string fingerprint, Guid? priorRun, Guid intentId, CancellationToken cancellationToken = default);
}
