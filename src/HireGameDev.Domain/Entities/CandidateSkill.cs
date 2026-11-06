using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class CandidateSkill
{
    public Guid CandidateId { get; set; }

    public CandidateProfile? Candidate { get; set; }

    public int SkillId { get; set; }

    public Skill? Skill { get; set; }

    public SkillProficiency Proficiency { get; set; } = SkillProficiency.Working;

    public decimal? YearsUsed { get; set; }

    public short? LastUsedYear { get; set; }

    public SkillEvidenceLevel EvidenceLevel { get; set; } = SkillEvidenceLevel.Claimed;

    public bool IsPrimary { get; set; }
}
