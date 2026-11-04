using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class Skill
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public SkillCategory Category { get; set; }

    public string? Subcategory { get; set; }

    public int? ParentSkillId { get; set; }

    public Skill? ParentSkill { get; set; }

    public ICollection<Skill> ChildSkills { get; set; } = new List<Skill>();

    public string[] Aliases { get; set; } = [];

    public decimal IdfWeight { get; set; } = 1.00m;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<SkillRelation> OutgoingRelations { get; set; } = new List<SkillRelation>();

    public ICollection<SkillRelation> IncomingRelations { get; set; } = new List<SkillRelation>();
}
