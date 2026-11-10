namespace HireGameDev.Domain.Entities;

public sealed class StudioSkill
{
    public Guid StudioId { get; set; }

    public StudioProfile Studio { get; set; } = null!;

    public int SkillId { get; set; }

    public Skill Skill { get; set; } = null!;
}
