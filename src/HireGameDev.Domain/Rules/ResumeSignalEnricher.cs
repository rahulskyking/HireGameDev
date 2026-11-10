using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Rules;

public sealed record ResumeCityCatalogEntry(
    int CityId,
    string Name,
    string Slug,
    string State,
    string StateCode,
    IReadOnlyList<string> Aliases,
    bool IsHub);

/// <summary>
/// Pure game-dev signal inference rules applied to extracted resume drafts (both offline <c>Dev</c> and live LLM providers):
/// non-overlapping work-duration math, platform token canonicalization, primary discipline scoring, and Indian city matching.
/// </summary>
public static class ResumeSignalEnricher
{
    private static readonly (string Token, string Platform)[] PlatformKeywords =
    [
        // PC
        ("steam", "pc"),
        ("epic games store", "pc"),
        ("windows", "pc"),
        ("macos", "pc"),
        ("linux", "pc"),
        ("steam deck", "pc"),
        ("pc", "pc"),

        // Console
        ("playstation", "console"),
        ("ps5", "console"),
        ("ps4", "console"),
        ("xbox series", "console"),
        ("xbox one", "console"),
        ("xbox", "console"),
        ("nintendo switch", "console"),
        ("switch", "console"),
        ("console", "console"),
        ("consoles", "console"),

        // Android
        ("google play", "android"),
        ("android", "android"),

        // iOS
        ("app store", "ios"),
        ("iphone", "ios"),
        ("ipad", "ios"),
        ("apple arcade", "ios"),
        ("ios", "ios"),

        // Web
        ("webgl", "web"),
        ("webgpu", "web"),
        ("html5", "web"),
        ("browser", "web"),
        ("poki", "web"),
        ("crazygames", "web"),
        ("web", "web")
    ];

    /// <summary>
    /// Computes merged non-overlapping professional experience in years, snapped to <c>0.5</c>-year increments
    /// within <c>[0, 50]</c>. Overlapping concurrent roles are merged so double-employment never inflates seniority.
    /// </summary>
    public static decimal? ComputeMergedExperienceYears(
        IReadOnlyList<(DateOnly Start, DateOnly End)> intervals)
    {
        if (intervals is null || intervals.Count == 0)
        {
            return null;
        }

        var valid = new List<(DateOnly Start, DateOnly End)>(intervals.Count);
        foreach (var (start, end) in intervals)
        {
            var clampedEnd = end < start ? start : end;
            valid.Add((start, clampedEnd));
        }

        valid.Sort(static (a, b) =>
        {
            var cmp = a.Start.CompareTo(b.Start);
            return cmp != 0 ? cmp : a.End.CompareTo(b.End);
        });

        var merged = new List<(DateOnly Start, DateOnly End)>(valid.Count) { valid[0] };
        for (var i = 1; i < valid.Count; i++)
        {
            var current = valid[i];
            var last = merged[^1];
            if (current.Start <= last.End.AddDays(15))
            {
                var maxEnd = current.End > last.End ? current.End : last.End;
                merged[^1] = (last.Start, maxEnd);
            }
            else
            {
                merged.Add(current);
            }
        }

        var totalMonths = 0;
        foreach (var (start, end) in merged)
        {
            var months = ((end.Year - start.Year) * 12) + (end.Month - start.Month) + 1;
            if (months > 0)
            {
                totalMonths += months;
            }
        }

        if (totalMonths <= 0)
        {
            return null;
        }

        var rawYears = totalMonths / 12.0m;
        var snapped = decimal.Round(rawYears * 2.0m, 0, MidpointRounding.AwayFromZero) / 2.0m;
        if (snapped < CandidateProfileRules.MinYearsExperience)
        {
            snapped = CandidateProfileRules.MinYearsExperience;
        }
        else if (snapped > CandidateProfileRules.MaxYearsExperience)
        {
            snapped = CandidateProfileRules.MaxYearsExperience;
        }

        return snapped;
    }

    /// <summary>
    /// Normalizes free-text platform names and optional title/project description text into canonical
    /// PostgreSQL <c>shipped_titles.platforms</c> values (<c>pc</c>, <c>android</c>, <c>ios</c>, <c>console</c>, <c>web</c>).
    /// </summary>
    public static IReadOnlyList<string> NormalizeGamePlatforms(
        IEnumerable<string>? explicitPlatforms,
        string? contextText = null)
    {
        var found = new List<string>(5);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddPlatform(string canonical)
        {
            if (CandidateProfileRules.IsValidGamePlatform(canonical) && seen.Add(canonical))
            {
                found.Add(canonical);
            }
        }

        if (explicitPlatforms is not null)
        {
            foreach (var raw in explicitPlatforms)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var token = raw.Trim().ToLowerInvariant();
                if (CandidateProfileRules.IsValidGamePlatform(token))
                {
                    AddPlatform(token);
                    continue;
                }

                if (token == "mobile")
                {
                    AddPlatform("android");
                    AddPlatform("ios");
                    continue;
                }

                foreach (var (keyword, platform) in PlatformKeywords)
                {
                    if (SkillEvidenceTextMatcher.ContainsWholeSkillTerm(token, keyword))
                    {
                        AddPlatform(platform);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(contextText))
        {
            foreach (var (keyword, platform) in PlatformKeywords)
            {
                if (SkillEvidenceTextMatcher.ContainsWholeSkillTerm(contextText, keyword))
                {
                    AddPlatform(platform);
                }
            }

            if (SkillEvidenceTextMatcher.ContainsWholeSkillTerm(contextText, "mobile") &&
                !seen.Contains("android") &&
                !seen.Contains("ios"))
            {
                AddPlatform("android");
                AddPlatform("ios");
            }
        }

        return found;
    }

    public static DisciplineCategory? TryParseExplicitDiscipline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return normalized switch
        {
            "programming" or "engineering" or "software" or "developer" => DisciplineCategory.Programming,
            "art" or "tech_art" or "technical_art" or "animation" or "vfx" or "3d_art" or "2d_art" => DisciplineCategory.Art,
            "design" or "game_design" or "level_design" or "narrative_design" or "system_design" => DisciplineCategory.Design,
            "audio" or "sound" or "sound_design" or "music" => DisciplineCategory.Audio,
            "qa" or "quality_assurance" or "testing" => DisciplineCategory.Qa,
            "production" or "producer" or "project_management" => DisciplineCategory.Production,
            _ => null
        };
    }

    /// <summary>
    /// Infers the candidate's primary game-dev discipline from their headline, role titles, and matched skill categories.
    /// </summary>
    public static DisciplineCategory? InferPrimaryDiscipline(
        string? headline,
        IReadOnlyList<string> roleTitles,
        IReadOnlyList<SkillCategory> matchedCategories)
    {
        var scores = new Dictionary<DisciplineCategory, int>
        {
            [DisciplineCategory.Programming] = 0,
            [DisciplineCategory.Art] = 0,
            [DisciplineCategory.Design] = 0,
            [DisciplineCategory.Audio] = 0,
            [DisciplineCategory.Qa] = 0,
            [DisciplineCategory.Production] = 0
        };

        static void ScoreRoleText(string? text, int weight, Dictionary<DisciplineCategory, int> map)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (ContainsAnyTerm(text, "technical artist", "tech artist", "3d artist", "2d artist", "environment artist", "character artist", "concept artist", "vfx artist", "animator", "rigger", "lighting artist", "ui artist", "artist"))
            {
                map[DisciplineCategory.Art] += 4 * weight;
            }

            if (ContainsAnyTerm(text, "sound designer", "audio designer", "composer", "audio programmer", "audio engineer", "sfx", "wwise", "fmod"))
            {
                map[DisciplineCategory.Audio] += 4 * weight;
            }

            if (ContainsAnyTerm(text, "qa", "quality assurance", "game tester", "test engineer", "sdet", "certification tester"))
            {
                map[DisciplineCategory.Qa] += 4 * weight;
            }

            if (ContainsAnyTerm(text, "producer", "production", "scrum master", "project manager", "liveops manager", "program manager"))
            {
                map[DisciplineCategory.Production] += 4 * weight;
            }

            if (ContainsAnyTerm(text, "game designer", "level designer", "systems designer", "combat designer", "economy designer", "narrative designer", "monetization designer", "ux designer"))
            {
                map[DisciplineCategory.Design] += 4 * weight;
            }

            if (ContainsAnyTerm(text, "programmer", "software engineer", "game developer", "unity developer", "unreal developer", "gameplay engineer", "engine programmer", "graphics programmer", "network engineer", "backend engineer", "tools programmer", "c++", "c#"))
            {
                map[DisciplineCategory.Programming] += 4 * weight;
            }
        }

        ScoreRoleText(headline, weight: 3, scores);
        for (var i = 0; i < roleTitles.Count; i++)
        {
            var roleWeight = i == 0 ? 2 : 1;
            ScoreRoleText(roleTitles[i], roleWeight, scores);
        }

        foreach (var cat in matchedCategories)
        {
            switch (cat)
            {
                case SkillCategory.Language:
                case SkillCategory.Devops:
                    scores[DisciplineCategory.Programming] += 2;
                    break;
                case SkillCategory.ArtTool:
                    scores[DisciplineCategory.Art] += 3;
                    break;
                case SkillCategory.DesignAudioTool:
                    scores[DisciplineCategory.Design] += 1;
                    scores[DisciplineCategory.Audio] += 1;
                    break;
            }
        }

        DisciplineCategory? best = null;
        var bestScore = 0;
        foreach (var kvp in scores)
        {
            if (kvp.Value > bestScore)
            {
                bestScore = kvp.Value;
                best = kvp.Key;
            }
        }

        return bestScore > 0 ? best : null;
    }

    /// <summary>
    /// Matches an explicit city string or header/location lines against active Indian <c>cities</c> (prioritizing hubs and exact/alias matches).
    /// </summary>
    public static int? MatchIndianCityId(
        string? explicitCity,
        string? fallbackLocationText,
        IReadOnlyList<ResumeCityCatalogEntry> cities)
    {
        if (cities is null || cities.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(explicitCity))
        {
            var trimmed = explicitCity.Trim();
            var commaIdx = trimmed.IndexOf(',');
            var primaryPart = commaIdx > 0 ? trimmed[..commaIdx].Trim() : trimmed;

            foreach (var city in cities)
            {
                if (string.Equals(city.Name, primaryPart, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(city.Slug, primaryPart, StringComparison.OrdinalIgnoreCase) ||
                    city.Aliases.Any(a => string.Equals(a, primaryPart, StringComparison.OrdinalIgnoreCase)))
                {
                    return city.CityId;
                }
            }

            foreach (var city in cities)
            {
                if (SkillEvidenceTextMatcher.ContainsAnySkillTerm(trimmed, city.Name, city.Aliases))
                {
                    return city.CityId;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackLocationText))
        {
            // Check gaming hubs first when scanning broader header/experience text.
            foreach (var city in cities.OrderByDescending(c => c.IsHub))
            {
                if (SkillEvidenceTextMatcher.ContainsAnySkillTerm(fallbackLocationText, city.Name, city.Aliases))
                {
                    return city.CityId;
                }
            }
        }

        return null;
    }

    private static bool ContainsAnyTerm(string text, params string[] terms)
    {
        for (var i = 0; i < terms.Length; i++)
        {
            if (SkillEvidenceTextMatcher.ContainsWholeSkillTerm(text, terms[i]))
            {
                return true;
            }
        }

        return false;
    }
}
