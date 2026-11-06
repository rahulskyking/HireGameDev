namespace HireGameDev.Domain.Entities;

/// <summary>
/// Relational join table mapping a PortfolioLink to the canonical Skills it evidences (ADR-014).
/// Replaces raw int[] skill_ids to guarantee FK referential integrity for M1.5 EvidenceService and skill merges.
/// </summary>
public sealed class PortfolioLinkSkill
{
    public Guid PortfolioLinkId { get; set; }

    public PortfolioLink? PortfolioLink { get; set; }

    public int SkillId { get; set; }

    public Skill? Skill { get; set; }
}
