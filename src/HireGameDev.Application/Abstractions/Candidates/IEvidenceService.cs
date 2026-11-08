using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Candidates;

/// <summary>
/// Recomputes <c>candidate_skills.evidence_level</c> (<c>Claimed = 0</c>, <c>Backed = 1</c>, <c>Verified = 2</c>)
/// from shipped titles (including NDA-protected titles), portfolio skill tags, work experience descriptions,
/// and any registered <see cref="IVerifiedSkillEvidenceProvider"/> implementations.
/// </summary>
public interface IEvidenceService
{
    /// <summary>
    /// Evaluates current tracked + persisted candidate evidence and updates <c>CandidateSkill.EvidenceLevel</c>
    /// in-memory within the caller's active <c>DbContext</c> unit of work / transaction without flushing early.
    /// </summary>
    Task<IReadOnlyDictionary<int, SkillEvidenceLevel>> RecomputeAndApplyAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes and persists <c>CandidateSkill.EvidenceLevel</c> for standalone callers
    /// (such as Phase 5 credit verification or Phase 7 skill challenge completion).
    /// </summary>
    Task<IReadOnlyDictionary<int, SkillEvidenceLevel>> RecomputeAndSaveAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);
}
