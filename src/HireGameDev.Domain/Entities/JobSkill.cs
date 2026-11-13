using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class JobSkill
{
    public Guid JobId { get; set; }

    public int SkillId { get; set; }

    public bool IsRequired { get; set; } = true;

    public SkillProficiency MinProficiency { get; set; } = SkillProficiency.Working;
}
