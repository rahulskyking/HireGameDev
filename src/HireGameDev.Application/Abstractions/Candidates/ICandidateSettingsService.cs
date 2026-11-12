using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;

namespace HireGameDev.Application.Abstractions.Candidates;

/// <summary>
/// Candidate discoverability, compensation privacy, and employer-blocklist service (M1.11 / ADR-020).
/// </summary>
public interface ICandidateSettingsService
{
    Task<Result<CandidateSettingsSnapshot>> GetSettingsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<CandidateSettingsSnapshot>> UpdatePrivacySettingsAsync(
        UpdateCandidatePrivacySettingsCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<BlockableStudioSearchItem>>> SearchStudiosToBlockAsync(
        Guid userId,
        string? query,
        CancellationToken cancellationToken = default);

    Task<Result<BlockedStudioItem>> BlockStudioAsync(
        BlockStudioCommand command,
        CancellationToken cancellationToken = default);

    Task<Result> UnblockStudioAsync(
        UnblockStudioCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates whether a signed-in studio user can discover/view a candidate outside an explicit job application:
    /// candidate must be active, discoverable, &gt;= 70% complete, and the studio viewer must belong to at least one
    /// active Verified studio while belonging to zero active studios blocked by the candidate.
    /// </summary>
    Task<bool> CanStudioViewerDiscoverCandidateAsync(
        Guid candidateId,
        Guid studioViewerUserId,
        CancellationToken cancellationToken = default);
}
