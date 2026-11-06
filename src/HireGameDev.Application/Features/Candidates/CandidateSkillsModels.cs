using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Candidates;

public sealed record CandidateSkillItem(
    int SkillId,
    string SkillName,
    string SkillSlug,
    SkillCategory Category,
    SkillProficiency Proficiency,
    decimal? YearsUsed,
    short? LastUsedYear,
    SkillEvidenceLevel EvidenceLevel,
    bool IsPrimary);

public sealed record CandidateSkillSelectionInput(
    int SkillId,
    SkillProficiency Proficiency,
    decimal? YearsUsed,
    short? LastUsedYear,
    bool IsPrimary);

public sealed record SaveCandidateSkillsCommand(
    Guid UserId,
    IReadOnlyList<CandidateSkillSelectionInput> Skills);
