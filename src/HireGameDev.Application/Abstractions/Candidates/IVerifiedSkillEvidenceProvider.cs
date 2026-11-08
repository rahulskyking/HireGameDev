namespace HireGameDev.Application.Abstractions.Candidates;

/// <summary>
/// Pluggable contributor for independently verified candidate skills (e.g., Phase 7 passed skill challenges).
/// Skills returned by any registered <see cref="IVerifiedSkillEvidenceProvider"/> receive
/// <see cref="Domain.Enums.SkillEvidenceLevel.Verified"/> and are never downgraded by Step 2, 3, or 4 profile saves.
/// </summary>
public interface IVerifiedSkillEvidenceProvider
{
    Task<IReadOnlySet<int>> GetVerifiedSkillIdsAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);
}
