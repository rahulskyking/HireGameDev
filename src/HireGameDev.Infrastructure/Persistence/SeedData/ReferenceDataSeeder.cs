using System.Reflection;
using System.Text.Json;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireGameDev.Infrastructure.Persistence.SeedData;

internal sealed class ReferenceDataSeeder : IReferenceDataSeeder
{
    private const string CitiesResourceSuffix = "cities.seed.json";
    private const string SkillsResourceSuffix = "skills.seed.json";
    private const string RelationsResourceSuffix = "skill_relations.seed.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _dbContext;
    private readonly IReferenceDataService _referenceDataService;
    private readonly IClock _clock;
    private readonly ILogger<ReferenceDataSeeder> _logger;

    public ReferenceDataSeeder(
        AppDbContext dbContext,
        IReferenceDataService referenceDataService,
        IClock clock,
        ILogger<ReferenceDataSeeder> logger)
    {
        _dbContext = dbContext;
        _referenceDataService = referenceDataService;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var appliedMigrations = await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (!appliedMigrations.Any(m => m.EndsWith("M0_4_ReferenceData", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "Skipping M0.4 reference data seed because migration M0_4_ReferenceData has not been applied yet. Run 'dotnet ef database update'.");
            return;
        }

        var citySeeds = LoadEmbeddedSeedList<CitySeedEntry>(CitiesResourceSuffix);
        var skillSeeds = LoadEmbeddedSeedList<SkillSeedEntry>(SkillsResourceSuffix);
        var relationSeeds = LoadEmbeddedSeedList<SkillRelationSeedEntry>(RelationsResourceSuffix);

        ValidateSeedManifests(citySeeds, skillSeeds, relationSeeds);

        var insertedCities = await UpsertCitiesAsync(citySeeds, cancellationToken);
        var insertedSkills = await UpsertSkillsAsync(skillSeeds, cancellationToken);
        var insertedRelations = await UpsertSkillRelationsAsync(relationSeeds, cancellationToken);

        _referenceDataService.InvalidateCache();

        _logger.LogInformation(
            "M0.4 reference data verified: {CityCount} cities ({HubCount} hubs), {SkillCount} skills, {RelationCount} directional relations (newly inserted: {InsertedCities} cities, {InsertedSkills} skills, {InsertedRelations} relations).",
            citySeeds.Count,
            citySeeds.Count(c => c.IsHub),
            skillSeeds.Count,
            relationSeeds.Count,
            insertedCities,
            insertedSkills,
            insertedRelations);
    }

    private async Task<int> UpsertCitiesAsync(
        IReadOnlyList<CitySeedEntry> seeds,
        CancellationToken cancellationToken)
    {
        var existingBySlug = await _dbContext.Cities
            .ToDictionaryAsync(c => c.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var aliases = NormalizeAliases(seed.Aliases, seed.Name);

            if (!existingBySlug.TryGetValue(seed.Slug, out var city))
            {
                city = new City
                {
                    Name = seed.Name.Trim(),
                    Slug = seed.Slug.Trim().ToLowerInvariant(),
                    State = seed.State.Trim(),
                    StateCode = seed.StateCode.Trim().ToUpperInvariant(),
                    CountryCode = "IN",
                    MetroGroup = NormalizeOptional(seed.MetroGroup),
                    Aliases = aliases,
                    IsHub = seed.IsHub,
                    SortOrder = seed.SortOrder,
                    IsActive = true
                };

                _dbContext.Cities.Add(city);
                inserted++;
            }
            else
            {
                var metroGroup = NormalizeOptional(seed.MetroGroup);
                if (!string.Equals(city.Name, seed.Name.Trim(), StringComparison.Ordinal) ||
                    !string.Equals(city.State, seed.State.Trim(), StringComparison.Ordinal) ||
                    !string.Equals(city.StateCode, seed.StateCode.Trim().ToUpperInvariant(), StringComparison.Ordinal) ||
                    !string.Equals(city.MetroGroup, metroGroup, StringComparison.Ordinal) ||
                    city.IsHub != seed.IsHub ||
                    city.SortOrder != seed.SortOrder ||
                    !city.Aliases.SequenceEqual(aliases, StringComparer.Ordinal))
                {
                    city.Name = seed.Name.Trim();
                    city.State = seed.State.Trim();
                    city.StateCode = seed.StateCode.Trim().ToUpperInvariant();
                    city.MetroGroup = metroGroup;
                    city.Aliases = aliases;
                    city.IsHub = seed.IsHub;
                    city.SortOrder = seed.SortOrder;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private async Task<int> UpsertSkillsAsync(
        IReadOnlyList<SkillSeedEntry> seeds,
        CancellationToken cancellationToken)
    {
        var existingBySlug = await _dbContext.Skills
            .ToDictionaryAsync(s => s.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var inserted = 0;
        var now = _clock.UtcNow;

        // Pass 1: Upsert core Skill rows without ParentSkillId so all identity keys exist first.
        foreach (var seed in seeds)
        {
            var slug = seed.Slug.Trim().ToLowerInvariant();
            var category = SnakeCaseEnumValueConverter<SkillCategory>.FromSnakeCaseValue(seed.Category.Trim());
            var subcategory = NormalizeOptional(seed.Subcategory);
            var aliases = NormalizeAliases(seed.Aliases, seed.Name);

            if (!existingBySlug.TryGetValue(slug, out var skill))
            {
                skill = new Skill
                {
                    Name = seed.Name.Trim(),
                    Slug = slug,
                    Category = category,
                    Subcategory = subcategory,
                    Aliases = aliases,
                    IdfWeight = 1.00m,
                    IsActive = true,
                    CreatedAt = now
                };

                _dbContext.Skills.Add(skill);
                existingBySlug[slug] = skill;
                inserted++;
            }
            else
            {
                if (!string.Equals(skill.Name, seed.Name.Trim(), StringComparison.Ordinal) ||
                    skill.Category != category ||
                    !string.Equals(skill.Subcategory, subcategory, StringComparison.Ordinal) ||
                    !skill.Aliases.SequenceEqual(aliases, StringComparer.Ordinal))
                {
                    skill.Name = seed.Name.Trim();
                    skill.Category = category;
                    skill.Subcategory = subcategory;
                    skill.Aliases = aliases;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Pass 2: Wire ParentSkillId hierarchy now that every skill has a generated PostgreSQL identity Id.
        var parentUpdated = false;
        foreach (var seed in seeds)
        {
            var slug = seed.Slug.Trim().ToLowerInvariant();
            var skill = existingBySlug[slug];

            int? desiredParentId = null;
            if (!string.IsNullOrWhiteSpace(seed.ParentSlug))
            {
                desiredParentId = existingBySlug[seed.ParentSlug.Trim().ToLowerInvariant()].Id;
            }

            if (skill.ParentSkillId != desiredParentId)
            {
                skill.ParentSkillId = desiredParentId;
                parentUpdated = true;
            }
        }

        if (parentUpdated)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return inserted;
    }

    private async Task<int> UpsertSkillRelationsAsync(
        IReadOnlyList<SkillRelationSeedEntry> seeds,
        CancellationToken cancellationToken)
    {
        var skillIdBySlug = await _dbContext.Skills
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Slug, s => s.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var existingRelations = await _dbContext.SkillRelations
            .ToDictionaryAsync(r => (r.SkillId, r.RelatedSkillId), cancellationToken);

        var inserted = 0;

        foreach (var seed in seeds)
        {
            var skillId = skillIdBySlug[seed.SkillSlug.Trim().ToLowerInvariant()];
            var relatedSkillId = skillIdBySlug[seed.RelatedSkillSlug.Trim().ToLowerInvariant()];
            var relationType = SnakeCaseEnumValueConverter<SkillRelationType>.FromSnakeCaseValue(seed.RelationType.Trim());
            var weight = decimal.Round(seed.Weight, 2, MidpointRounding.AwayFromZero);

            if (!existingRelations.TryGetValue((skillId, relatedSkillId), out var existing))
            {
                _dbContext.SkillRelations.Add(new SkillRelation
                {
                    SkillId = skillId,
                    RelatedSkillId = relatedSkillId,
                    RelationType = relationType,
                    Weight = weight
                });

                inserted++;
            }
            else if (existing.RelationType != relationType || existing.Weight != weight)
            {
                existing.RelationType = relationType;
                existing.Weight = weight;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private static List<T> LoadEmbeddedSeedList<T>(string resourceSuffix)
    {
        var assembly = typeof(ReferenceDataSeeder).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Embedded seed manifest ending with '{resourceSuffix}' was not found in {assembly.GetName().Name}.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open stream for embedded resource '{resourceName}'.");

        var items = JsonSerializer.Deserialize<List<T>>(stream, JsonOptions);
        if (items is null || items.Count == 0)
        {
            throw new InvalidOperationException($"Seed manifest '{resourceSuffix}' deserialized to an empty list.");
        }

        return items;
    }

    private static void ValidateSeedManifests(
        IReadOnlyList<CitySeedEntry> cities,
        IReadOnlyList<SkillSeedEntry> skills,
        IReadOnlyList<SkillRelationSeedEntry> relations)
    {
        var citySlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cityStateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cityTermOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var city in cities)
        {
            var slug = city.Slug.Trim();
            if (!IsValidSlug(slug))
            {
                throw new InvalidOperationException($"Invalid city slug '{city.Slug}'.");
            }

            if (!citySlugs.Add(slug))
            {
                throw new InvalidOperationException($"Duplicate city slug '{slug}' in cities.seed.json.");
            }

            var cityStateKey = $"{city.Name.Trim()}|{city.State.Trim()}";
            if (!cityStateKeys.Add(cityStateKey))
            {
                throw new InvalidOperationException($"Duplicate city/state '{cityStateKey}' in cities.seed.json.");
            }

            foreach (var term in EnumerateSearchTerms(city.Name, city.Aliases))
            {
                if (cityTermOwner.TryGetValue(term, out var existingOwner) &&
                    !string.Equals(existingOwner, slug, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"City alias/name collision on '{term}' between '{existingOwner}' and '{slug}'.");
                }

                cityTermOwner[term] = slug;
            }
        }

        var skillBySlug = new Dictionary<string, SkillSeedEntry>(StringComparer.OrdinalIgnoreCase);
        var skillNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skillTermOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in skills)
        {
            var slug = skill.Slug.Trim();
            if (!IsValidSlug(slug))
            {
                throw new InvalidOperationException($"Invalid skill slug '{skill.Slug}'.");
            }

            if (!skillBySlug.TryAdd(slug, skill))
            {
                throw new InvalidOperationException($"Duplicate skill slug '{slug}' in skills.seed.json.");
            }

            if (!skillNames.Add(skill.Name.Trim()))
            {
                throw new InvalidOperationException($"Duplicate skill name '{skill.Name}' in skills.seed.json.");
            }

            // Validates category string against SkillCategory enum
            _ = SnakeCaseEnumValueConverter<SkillCategory>.FromSnakeCaseValue(skill.Category.Trim());

            foreach (var term in EnumerateSearchTerms(skill.Name, skill.Aliases))
            {
                if (skillTermOwner.TryGetValue(term, out var existingOwner) &&
                    !string.Equals(existingOwner, slug, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Skill alias/name collision on '{term}' between '{existingOwner}' and '{slug}'.");
                }

                skillTermOwner[term] = slug;
            }
        }

        foreach (var skill in skills)
        {
            if (string.IsNullOrWhiteSpace(skill.ParentSlug))
            {
                continue;
            }

            var parentSlug = skill.ParentSlug.Trim();
            if (string.Equals(parentSlug, skill.Slug.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Skill '{skill.Slug}' cannot reference itself as parent.");
            }

            if (!skillBySlug.ContainsKey(parentSlug))
            {
                throw new InvalidOperationException(
                    $"Skill '{skill.Slug}' references unknown parentSlug '{parentSlug}'.");
            }
        }

        var seenPairs = new HashSet<(string, string)>();

        foreach (var rel in relations)
        {
            var skillSlug = rel.SkillSlug.Trim().ToLowerInvariant();
            var relatedSlug = rel.RelatedSkillSlug.Trim().ToLowerInvariant();

            if (string.Equals(skillSlug, relatedSlug, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Self skill relation is not allowed for '{skillSlug}'.");
            }

            if (!seenPairs.Add((skillSlug, relatedSlug)))
            {
                throw new InvalidOperationException(
                    $"Duplicate skill relation ('{skillSlug}', '{relatedSlug}') in skill_relations.seed.json.");
            }

            if (!skillBySlug.ContainsKey(skillSlug))
            {
                throw new InvalidOperationException($"Skill relation references unknown skillSlug '{skillSlug}'.");
            }

            if (!skillBySlug.ContainsKey(relatedSlug))
            {
                throw new InvalidOperationException($"Skill relation references unknown relatedSkillSlug '{relatedSlug}'.");
            }

            var relationType = SnakeCaseEnumValueConverter<SkillRelationType>.FromSnakeCaseValue(rel.RelationType.Trim());
            var (minWeight, maxWeight) = relationType switch
            {
                SkillRelationType.Implies => (0.85m, 0.95m),
                SkillRelationType.Superset => (0.70m, 0.80m),
                SkillRelationType.PeerTool => (0.50m, 0.65m),
                SkillRelationType.Transferable => (0.30m, 0.45m),
                _ => (0.01m, 1.00m)
            };

            if (rel.Weight < minWeight || rel.Weight > maxWeight)
            {
                throw new InvalidOperationException(
                    $"Skill relation ('{skillSlug}' <- '{relatedSlug}') weight {rel.Weight} is outside {relationType} calibration band [{minWeight}, {maxWeight}].");
            }
        }
    }

    private static IEnumerable<string> EnumerateSearchTerms(string primaryName, string[]? aliases)
    {
        yield return primaryName.Trim();

        if (aliases is null)
        {
            yield break;
        }

        foreach (var alias in aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return alias.Trim();
            }
        }
    }

    private static string[] NormalizeAliases(string[]? rawAliases, string primaryName)
    {
        if (rawAliases is null || rawAliases.Length == 0)
        {
            return [];
        }

        return rawAliases
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Where(a => !string.Equals(a, primaryName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidSlug(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-'))
            {
                return false;
            }
        }

        return true;
    }
}
