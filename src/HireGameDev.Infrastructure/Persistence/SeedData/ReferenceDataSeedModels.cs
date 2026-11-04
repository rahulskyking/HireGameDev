namespace HireGameDev.Infrastructure.Persistence.SeedData;

internal sealed record CitySeedEntry(
    string Name,
    string Slug,
    string State,
    string StateCode,
    string? MetroGroup,
    string[]? Aliases,
    bool IsHub,
    short SortOrder);

internal sealed record SkillSeedEntry(
    string Name,
    string Slug,
    string Category,
    string? Subcategory,
    string? ParentSlug,
    string[]? Aliases);

internal sealed record SkillRelationSeedEntry(
    string SkillSlug,
    string RelatedSkillSlug,
    string RelationType,
    decimal Weight);
