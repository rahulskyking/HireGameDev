using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

/// <summary>
/// Submitted portfolio proof item on a candidate profile (Blueprint v2.0 Section 5, portfolio_links).
/// Represents candidate-submitted proof (supports Backed 0.90x skill weight in M1.5, never Verified 1.00x).
/// </summary>
public sealed class PortfolioLink
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public CandidateProfile? Candidate { get; set; }

    public PortfolioLinkType LinkType { get; set; } = PortfolioLinkType.Website;

    public string Url { get; set; } = string.Empty;

    public string? Title { get; set; }

    public short SortOrder { get; set; }

    public ICollection<PortfolioLinkSkill> EvidencedSkills { get; set; } = new List<PortfolioLinkSkill>();
}
