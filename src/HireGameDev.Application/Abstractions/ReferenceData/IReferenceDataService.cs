using HireGameDev.Application.Common;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.ReferenceData;

public interface IReferenceDataService
{
    Task<IReadOnlyList<CityLookupItem>> GetActiveCitiesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CityLookupItem>> SearchCitiesAsync(
        string query,
        int maxResults = 15,
        CancellationToken cancellationToken = default);

    Task<CityLookupItem?> MatchCityByNameOrAliasAsync(
        string rawLocationText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillLookupItem>> GetActiveSkillsAsync(
        SkillCategory? category = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillLookupItem>> SearchSkillsAsync(
        string query,
        SkillCategory? category = null,
        int maxResults = 20,
        CancellationToken cancellationToken = default);

    Task<SkillLookupItem?> MatchSkillByNameOrAliasAsync(
        string rawSkillText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillRelationItem>> GetSkillRelationsAsync(
        CancellationToken cancellationToken = default);

    Task<ReferenceDataSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<string> ExportSkillsCsvAsync(
        CancellationToken cancellationToken = default);

    Task<Result<SkillCsvImportResult>> ImportSkillsCsvAsync(
        string csvContent,
        CancellationToken cancellationToken = default);

    void InvalidateCache();
}
