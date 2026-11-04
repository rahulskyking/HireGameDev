using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

/// <summary>
/// Directional partial-credit edge in the skill graph.
/// Meaning: a candidate who HAS <see cref="RelatedSkillId"/> earns <see cref="Weight"/> credit
/// toward a job requirement for <see cref="SkillId"/>.
/// </summary>
public sealed class SkillRelation
{
    public int SkillId { get; set; }

    public Skill Skill { get; set; } = null!;

    public int RelatedSkillId { get; set; }

    public Skill RelatedSkill { get; set; } = null!;

    public SkillRelationType RelationType { get; set; }

    public decimal Weight { get; set; }
}
