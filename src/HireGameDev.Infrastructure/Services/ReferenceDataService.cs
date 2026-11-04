using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HireGameDev.Infrastructure.Services;

internal sealed partial class ReferenceDataService : IReferenceDataService
{
    private const string CacheKey = "hgd:reference_data:snapshot:v1";
    private const string CsvHeader = "slug,name,category,subcategory,parent_slug,aliases,is_active";

    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;

    public ReferenceDataService(
        AppDbContext dbContext,
        IMemoryCache cache,
        IClock clock)
    {
        _dbContext = dbContext;
        _cache = cache;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CityLookupItem>> GetActiveCitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        return snapshot.Cities;
    }

    public async Task<IReadOnlyList<CityLookupItem>> SearchCitiesAsync(
        string query,
        int maxResults = 15,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(maxResults, 1, 100);
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            return snapshot.Cities.Take(limit).ToArray();
        }

        var q = query.Trim();

        return snapshot.Cities
            .Select(city => (City: city, Rank: ScoreCityMatch(city, q)))
            .Where(x => x.Rank < int.MaxValue)
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.City.IsHub)
            .ThenBy(x => x.City.SortOrder)
            .ThenBy(x => x.City.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.City)
            .ToArray();
    }

    public async Task<CityLookupItem?> MatchCityByNameOrAliasAsync(
        string rawLocationText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawLocationText))
        {
            return null;
        }

        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        var trimmed = rawLocationText.Trim();

        if (snapshot.CityByTerm.TryGetValue(trimmed, out var exact))
        {
            return exact;
        }

        // Handle composite location strings from resumes such as "Bangalore, Karnataka, India"
        foreach (var token in trimmed.Split([',', '/', '|', '-', '('], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (snapshot.CityByTerm.TryGetValue(token, out var tokenMatch))
            {
                return tokenMatch;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<SkillLookupItem>> GetActiveSkillsAsync(
        SkillCategory? category = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        if (category is null)
        {
            return snapshot.Skills;
        }

        return snapshot.Skills
            .Where(s => s.Category == category.Value)
            .ToArray();
    }

    public async Task<IReadOnlyList<SkillLookupItem>> SearchSkillsAsync(
        string query,
        SkillCategory? category = null,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(maxResults, 1, 100);
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);

        IEnumerable<SkillLookupItem> candidates = snapshot.Skills;
        if (category is not null)
        {
            candidates = candidates.Where(s => s.Category == category.Value);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return candidates.Take(limit).ToArray();
        }

        var q = query.Trim();

        return candidates
            .Select(skill => (Skill: skill, Rank: ScoreSkillMatch(skill, q)))
            .Where(x => x.Rank < int.MaxValue)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Skill.ParentSkillId is null ? 0 : 1)
            .ThenBy(x => x.Skill.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.Skill)
            .ToArray();
    }

    public async Task<SkillLookupItem?> MatchSkillByNameOrAliasAsync(
        string rawSkillText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSkillText))
        {
            return null;
        }

        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        return snapshot.SkillByTerm.TryGetValue(rawSkillText.Trim(), out var match)
            ? match
            : null;
    }

    public async Task<IReadOnlyList<SkillRelationItem>> GetSkillRelationsAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        return snapshot.SkillRelations;
    }

    public async Task<ReferenceDataSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        return new ReferenceDataSummary(
            ActiveCityCount: snapshot.Cities.Count,
            HubCityCount: snapshot.Cities.Count(c => c.IsHub),
            ActiveSkillCount: snapshot.Skills.Count,
            SkillRelationCount: snapshot.SkillRelations.Count);
    }

    public async Task<string> ExportSkillsCsvAsync(
        CancellationToken cancellationToken = default)
    {
        var skills = await _dbContext.Skills
            .AsNoTracking()
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var slugById = skills.ToDictionary(s => s.Id, s => s.Slug);

        var sb = new StringBuilder(skills.Count * 96);
        sb.AppendLine(CsvHeader);

        foreach (var skill in skills)
        {
            var categorySnake = SnakeCaseEnumValueConverter<SkillCategory>.ToSnakeCaseValue(skill.Category);
            var parentSlug = skill.ParentSkillId.HasValue && slugById.TryGetValue(skill.ParentSkillId.Value, out var pSlug)
                ? pSlug
                : string.Empty;
            var aliasesPipe = string.Join('|', skill.Aliases);

            sb.Append(EscapeCsvCell(skill.Slug)).Append(',')
              .Append(EscapeCsvCell(skill.Name)).Append(',')
              .Append(EscapeCsvCell(categorySnake)).Append(',')
              .Append(EscapeCsvCell(skill.Subcategory ?? string.Empty)).Append(',')
              .Append(EscapeCsvCell(parentSlug)).Append(',')
              .Append(EscapeCsvCell(aliasesPipe)).Append(',')
              .Append(skill.IsActive ? "true" : "false")
              .AppendLine();
        }

        return sb.ToString();
    }

    public async Task<Result<SkillCsvImportResult>> ImportSkillsCsvAsync(
        string csvContent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return Result.Failure<SkillCsvImportResult>(
                Error.Validation("ReferenceData.EmptyCsv", "CSV content cannot be empty."));
        }

        var rows = ParseCsvRows(csvContent);
        if (rows.Count <= 1)
        {
            return Result.Failure<SkillCsvImportResult>(
                Error.Validation("ReferenceData.MissingRows", "CSV must contain a header row and at least one skill row."));
        }

        var header = rows[0];
        if (header.Count < 7 || !string.Equals(header[0].Trim(), "slug", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<SkillCsvImportResult>(
                Error.Validation(
                    "ReferenceData.InvalidCsvHeader",
                    $"Expected CSV header: {CsvHeader}"));
        }

        var existingBySlug = await _dbContext.Skills
            .ToDictionaryAsync(s => s.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var warnings = new List<string>();
        var pendingParentSlugs = new List<(string Slug, string? ParentSlug)>();
        var now = _clock.UtcNow;

        for (var i = 1; i < rows.Count; i++)
        {
            var cols = rows[i];
            if (cols.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (cols.Count < 7)
            {
                skipped++;
                warnings.Add($"Row {i + 1}: expected 7 columns, found {cols.Count}.");
                continue;
            }

            var slug = cols[0].Trim().ToLowerInvariant();
            var name = cols[1].Trim();
            var categoryRaw = cols[2].Trim();
            var subcategory = string.IsNullOrWhiteSpace(cols[3]) ? null : cols[3].Trim();
            var parentSlug = string.IsNullOrWhiteSpace(cols[4]) ? null : cols[4].Trim().ToLowerInvariant();
            var aliases = cols[5]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var isActive = !bool.TryParse(cols[6].Trim(), out var parsedActive) || parsedActive;

            if (!SlugRegex().IsMatch(slug) || string.IsNullOrWhiteSpace(name))
            {
                skipped++;
                warnings.Add($"Row {i + 1}: invalid slug '{slug}' or empty skill name.");
                continue;
            }

            SkillCategory category;
            try
            {
                category = SnakeCaseEnumValueConverter<SkillCategory>.FromSnakeCaseValue(categoryRaw);
            }
            catch (InvalidOperationException)
            {
                skipped++;
                warnings.Add($"Row {i + 1}: unknown category '{categoryRaw}' for skill '{slug}'.");
                continue;
            }

            if (!existingBySlug.TryGetValue(slug, out var skill))
            {
                skill = new Skill
                {
                    Slug = slug,
                    Name = name,
                    Category = category,
                    Subcategory = subcategory,
                    Aliases = aliases,
                    IdfWeight = 1.00m,
                    IsActive = isActive,
                    CreatedAt = now
                };

                _dbContext.Skills.Add(skill);
                existingBySlug[slug] = skill;
                inserted++;
            }
            else
            {
                skill.Name = name;
                skill.Category = category;
                skill.Subcategory = subcategory;
                skill.Aliases = aliases;
                skill.IsActive = isActive;
                updated++;
            }

            pendingParentSlugs.Add((slug, parentSlug));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var (slug, parentSlug) in pendingParentSlugs)
        {
            var skill = existingBySlug[slug];
            if (parentSlug is null)
            {
                skill.ParentSkillId = null;
            }
            else if (existingBySlug.TryGetValue(parentSlug, out var parentSkill) && parentSkill.Id != skill.Id)
            {
                skill.ParentSkillId = parentSkill.Id;
            }
            else
            {
                warnings.Add($"Skill '{slug}': parent_slug '{parentSlug}' was not found and was left unset.");
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache();

        return Result.Success(new SkillCsvImportResult(inserted, updated, skipped, warnings));
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
    }

    private async Task<CachedTaxonomySnapshot> GetOrCreateSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out CachedTaxonomySnapshot? cached) && cached is not null)
        {
            return cached;
        }

        var cityEntities = await _dbContext.Cities
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.IsHub)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var skillEntities = await _dbContext.Skills
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var skillSlugById = skillEntities.ToDictionary(s => s.Id, s => s.Slug);

        var relationEntities = await _dbContext.SkillRelations
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var cities = cityEntities
            .Select(c => new CityLookupItem(
                c.Id,
                c.Name,
                c.Slug,
                c.State,
                c.StateCode,
                c.CountryCode,
                c.MetroGroup,
                c.Aliases,
                c.IsHub,
                c.SortOrder))
            .ToArray();

        var skills = skillEntities
            .Select(s => new SkillLookupItem(
                s.Id,
                s.Name,
                s.Slug,
                s.Category,
                s.Subcategory,
                s.ParentSkillId,
                s.ParentSkillId.HasValue && skillSlugById.TryGetValue(s.ParentSkillId.Value, out var pSlug) ? pSlug : null,
                s.Aliases,
                s.IdfWeight))
            .ToArray();

        var relations = relationEntities
            .Where(r => skillSlugById.ContainsKey(r.SkillId) && skillSlugById.ContainsKey(r.RelatedSkillId))
            .Select(r => new SkillRelationItem(
                r.SkillId,
                skillSlugById[r.SkillId],
                r.RelatedSkillId,
                skillSlugById[r.RelatedSkillId],
                r.RelationType,
                r.Weight))
            .ToArray();

        var cityTermMap = new Dictionary<string, CityLookupItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var city in cities)
        {
            cityTermMap.TryAdd(city.Name, city);
            cityTermMap.TryAdd(city.Slug, city);
            foreach (var alias in city.Aliases)
            {
                cityTermMap.TryAdd(alias, city);
            }
        }

        var skillTermMap = new Dictionary<string, SkillLookupItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
        {
            skillTermMap.TryAdd(skill.Name, skill);
            skillTermMap.TryAdd(skill.Slug, skill);
            foreach (var alias in skill.Aliases)
            {
                skillTermMap.TryAdd(alias, skill);
            }
        }

        var snapshot = new CachedTaxonomySnapshot(
            cities,
            skills,
            relations,
            cityTermMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            skillTermMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));

        _cache.Set(CacheKey, snapshot, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(30),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
        });

        return snapshot;
    }

    private static int ScoreCityMatch(CityLookupItem city, string query)
    {
        if (string.Equals(city.Name, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(city.Slug, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (city.Aliases.Any(a => string.Equals(a, query, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        if (city.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            city.Aliases.Any(a => a.StartsWith(query, StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        if (city.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            city.Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            city.State.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return int.MaxValue;
    }

    private static int ScoreSkillMatch(SkillLookupItem skill, string query)
    {
        if (string.Equals(skill.Name, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(skill.Slug, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (skill.Aliases.Any(a => string.Equals(a, query, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        if (skill.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            skill.Aliases.Any(a => a.StartsWith(query, StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        if (skill.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            skill.Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            (skill.Subcategory?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
        {
            return 3;
        }

        return int.MaxValue;
    }

    private static string EscapeCsvCell(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static List<List<string>> ParseCsvRows(string csv)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        currentCell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentCell.Append(ch);
                }
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                }
                else if (ch is '\r' or '\n')
                {
                    if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    {
                        i++;
                    }

                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                }
                else
                {
                    currentCell.Append(ch);
                }
            }
        }

        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    private sealed record CachedTaxonomySnapshot(
        IReadOnlyList<CityLookupItem> Cities,
        IReadOnlyList<SkillLookupItem> Skills,
        IReadOnlyList<SkillRelationItem> SkillRelations,
        FrozenDictionary<string, CityLookupItem> CityByTerm,
        FrozenDictionary<string, SkillLookupItem> SkillByTerm);
}
