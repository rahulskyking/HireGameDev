using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.ReferenceData;

public sealed record CityLookupItem(
    int Id,
    string Name,
    string Slug,
    string State,
    string StateCode,
    string CountryCode,
    string? MetroGroup,
    IReadOnlyList<string> Aliases,
    bool IsHub,
    short SortOrder);

public sealed record SkillLookupItem(
    int Id,
    string Name,
    string Slug,
    SkillCategory Category,
    string? Subcategory,
    int? ParentSkillId,
    string? ParentSkillSlug,
    IReadOnlyList<string> Aliases,
    decimal IdfWeight);

public sealed record SkillRelationItem(
    int SkillId,
    string SkillSlug,
    int RelatedSkillId,
    string RelatedSkillSlug,
    SkillRelationType RelationType,
    decimal Weight);

public sealed record ReferenceDataSummary(
    int ActiveCityCount,
    int HubCityCount,
    int ActiveSkillCount,
    int SkillRelationCount);

public sealed record SkillCsvImportResult(
    int InsertedCount,
    int UpdatedCount,
    int SkippedCount,
    IReadOnlyList<string> Warnings);
