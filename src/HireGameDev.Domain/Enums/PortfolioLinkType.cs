namespace HireGameDev.Domain.Enums;

/// <summary>
/// Portfolio link categories (Blueprint v2.0 Section 5).
/// Member names map 1-for-1 via SnakeCaseEnumValueConverter to PostgreSQL CHECK values:
/// 'github', 'itchio', 'artstation', 'behance', 'youtube', 'website', 'linkedin', 'other'.
/// </summary>
public enum PortfolioLinkType
{
    Github = 0,
    Itchio = 1,
    Artstation = 2,
    Behance = 3,
    Youtube = 4,
    Website = 5,
    Linkedin = 6,
    Other = 7
}
