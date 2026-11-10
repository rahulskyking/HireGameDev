using System.Globalization;
using System.Text.Json;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

/// <summary>
/// Strict C# JSON validator and bounds sanitizer for the Resume AI LLM extraction contract
/// (Blueprint v2.0 Section 5 Steps 4–5). Rejects malformed or non-object JSON and clamps/filters
/// out-of-range dates, lengths, and numeric fields against PostgreSQL CHECK constraints.
/// </summary>
public static class ResumeExtractionJsonParser
{
    public const int MaxRawJsonLength = 65_536;
    private const int MinHeadlineLength = 3;
    private const int MaxHeadlineLength = 80;
    private const int MaxSkillNameLength = 80;
    private const int MaxRawSkills = 40;
    private const int MaxCompanyOrTitleLength = 160;
    private const int MaxInstitutionLength = 200;
    private const int MaxDegreeOrFieldLength = 160;
    private const int MaxShippedGameOrStudioLength = 160;
    private const int MaxShippedRoleLength = 120;

    public static bool TryParseAndSanitize(
        string? rawJson,
        int referenceUtcYear,
        out SanitizedResumeExtractionPayload? payload,
        out string? errorMessage)
    {
        payload = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errorMessage = "Resume extraction returned an empty response.";
            return false;
        }

        var unwrapped = UnwrapCodeFences(rawJson.Trim());
        if (unwrapped.Length == 0 || unwrapped.Length > MaxRawJsonLength)
        {
            errorMessage = "Resume extraction output exceeded allowed JSON bounds.";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(unwrapped, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 16
            });
        }
        catch (JsonException)
        {
            errorMessage = "Resume extraction returned invalid JSON.";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Resume extraction root element must be a JSON object.";
                return false;
            }

            var headline = ParseSanitizedHeadline(root);
            var yearsExperience = ParseHalfYearDecimal(
                root,
                "years_experience",
                CandidateProfileRules.MinYearsExperience,
                CandidateProfileRules.MaxYearsExperience);

            var skills = ParseSkills(root, referenceUtcYear);
            var experiences = ParseExperiences(root, referenceUtcYear);
            var education = ParseEducation(root, referenceUtcYear);
            var shippedTitles = ParseShippedTitles(root, referenceUtcYear);

            payload = new SanitizedResumeExtractionPayload(
                Headline: headline,
                YearsExperience: yearsExperience,
                Skills: skills,
                Experiences: experiences,
                Education: education,
                ShippedTitles: shippedTitles);

            return true;
        }
    }

    private static string? ParseSanitizedHeadline(JsonElement root)
    {
        var raw = TryGetOptionalString(root, "headline");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = CollapseWhitespace(StripRedactionPlaceholders(raw));
        if (cleaned.Length < MinHeadlineLength)
        {
            return null;
        }

        if (cleaned.Length > MaxHeadlineLength)
        {
            cleaned = cleaned[..MaxHeadlineLength].TrimEnd();
        }

        return cleaned.Length >= MinHeadlineLength ? cleaned : null;
    }

    private static IReadOnlyList<SanitizedResumeSkillExtraction> ParseSkills(
        JsonElement root,
        int referenceUtcYear)
    {
        if (!TryGetArray(root, "skills", out var array))
        {
            return [];
        }

        var maxAllowedYear = (short)Math.Min(CandidateProfileRules.MaxSkillLastUsedYear, referenceUtcYear + 1);
        var list = new List<SanitizedResumeSkillExtraction>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in array.EnumerateArray())
        {
            if (list.Count >= MaxRawSkills)
            {
                break;
            }

            if (item.ValueKind == JsonValueKind.String)
            {
                var strName = CollapseWhitespace(StripRedactionPlaceholders(item.GetString() ?? string.Empty));
                if (strName.Length is >= 1 and <= MaxSkillNameLength && seenNames.Add(strName))
                {
                    list.Add(new SanitizedResumeSkillExtraction(strName, null, null));
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var rawName = TryGetOptionalString(item, "name");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                continue;
            }

            var name = CollapseWhitespace(StripRedactionPlaceholders(rawName));
            if (name.Length is < 1 or > MaxSkillNameLength || !seenNames.Add(name))
            {
                continue;
            }

            var yearsUsed = ParseHalfYearDecimal(
                item,
                "years_used",
                CandidateProfileRules.MinYearsExperience,
                CandidateProfileRules.MaxYearsExperience);

            var lastUsedYear = ParseBoundedYear(
                item,
                "last_used_year",
                CandidateProfileRules.MinSkillLastUsedYear,
                maxAllowedYear);

            list.Add(new SanitizedResumeSkillExtraction(name, yearsUsed, lastUsedYear));
        }

        return list;
    }

    private static IReadOnlyList<SanitizedResumeExperienceExtraction> ParseExperiences(
        JsonElement root,
        int referenceUtcYear)
    {
        if (!TryGetArray(root, "experiences", out var array))
        {
            return [];
        }

        var maxYear = Math.Min(2100, referenceUtcYear + 1);
        var list = new List<SanitizedResumeExperienceExtraction>();

        foreach (var item in array.EnumerateArray())
        {
            if (list.Count >= CandidateProfileRules.MaxCandidateExperiences)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var company = TruncateClean(TryGetOptionalString(item, "company"), MaxCompanyOrTitleLength);
            var title = TruncateClean(TryGetOptionalString(item, "title"), MaxCompanyOrTitleLength);
            if (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var rawStart = TryGetOptionalString(item, "start");
            if (!TryParseYearMonthOrYear(rawStart, 1970, maxYear, out var startDate))
            {
                continue;
            }

            var rawEnd = TryGetOptionalString(item, "end");
            DateOnly? endDate = null;
            var isCurrent = false;

            if (string.IsNullOrWhiteSpace(rawEnd) || IsPresentToken(rawEnd))
            {
                isCurrent = true;
            }
            else if (TryParseYearMonthOrYear(rawEnd, 1970, maxYear, out var parsedEnd))
            {
                if (parsedEnd >= startDate)
                {
                    endDate = parsedEnd;
                }
                else
                {
                    // Guard against inverted dates from noisy PDF layouts.
                    endDate = startDate;
                }
            }
            else
            {
                // Unparseable non-null end token: keep row editable as non-current with startDate fallback
                // rather than violating ck_candidate_experiences_current_end_date.
                isCurrent = true;
            }

            var rawDesc = TryGetOptionalString(item, "description");
            string? description = null;
            if (!string.IsNullOrWhiteSpace(rawDesc))
            {
                var cleanedDesc = StripRedactionPlaceholders(rawDesc).Trim();
                if (cleanedDesc.Length > CandidateProfileRules.MaxExperienceDescriptionLength)
                {
                    cleanedDesc = cleanedDesc[..CandidateProfileRules.MaxExperienceDescriptionLength].TrimEnd();
                }

                if (cleanedDesc.Length > 0)
                {
                    description = cleanedDesc;
                }
            }

            list.Add(new SanitizedResumeExperienceExtraction(
                CompanyName: company,
                Title: title,
                StartDate: startDate,
                EndDate: endDate,
                IsCurrent: isCurrent,
                Description: description));
        }

        return list;
    }

    private static IReadOnlyList<SanitizedResumeEducationExtraction> ParseEducation(
        JsonElement root,
        int referenceUtcYear)
    {
        if (!TryGetArray(root, "education", out var array))
        {
            return [];
        }

        var maxEduYear = (short)Math.Min(CandidateProfileRules.MaxEducationYear, referenceUtcYear + 6);
        var list = new List<SanitizedResumeEducationExtraction>();

        foreach (var item in array.EnumerateArray())
        {
            if (list.Count >= CandidateProfileRules.MaxCandidateEducationEntries)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var institution = TruncateClean(TryGetOptionalString(item, "institution"), MaxInstitutionLength);
            var degree = TruncateClean(TryGetOptionalString(item, "degree"), MaxDegreeOrFieldLength);
            if (string.IsNullOrWhiteSpace(institution) || string.IsNullOrWhiteSpace(degree))
            {
                continue;
            }

            var field = TruncateClean(TryGetOptionalString(item, "field"), MaxDegreeOrFieldLength);
            var startYear = ParseBoundedYear(
                item,
                "start_year",
                CandidateProfileRules.MinEducationYear,
                maxEduYear);
            var endYear = ParseBoundedYear(
                item,
                "end_year",
                CandidateProfileRules.MinEducationYear,
                maxEduYear);

            if (startYear.HasValue && endYear.HasValue && endYear.Value < startYear.Value)
            {
                endYear = null;
            }

            list.Add(new SanitizedResumeEducationExtraction(
                Institution: institution,
                Degree: degree,
                Field: field,
                StartYear: startYear,
                EndYear: endYear));
        }

        return list;
    }

    private static IReadOnlyList<SanitizedResumeShippedTitleExtraction> ParseShippedTitles(
        JsonElement root,
        int referenceUtcYear)
    {
        if (!TryGetArray(root, "shipped_titles", out var array))
        {
            return [];
        }

        var maxReleaseYear = (short)Math.Min(CandidateProfileRules.MaxShippedTitleReleaseYear, referenceUtcYear + 2);
        var list = new List<SanitizedResumeShippedTitleExtraction>();

        foreach (var item in array.EnumerateArray())
        {
            if (list.Count >= CandidateProfileRules.MaxShippedTitles)
            {
                break;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var game = TruncateClean(TryGetOptionalString(item, "game"), MaxShippedGameOrStudioLength);
            if (string.IsNullOrWhiteSpace(game))
            {
                continue;
            }

            var studio = TruncateClean(TryGetOptionalString(item, "studio"), MaxShippedGameOrStudioLength);
            var role = TruncateClean(TryGetOptionalString(item, "role"), MaxShippedRoleLength);
            var year = ParseBoundedYear(
                item,
                "year",
                CandidateProfileRules.MinShippedTitleReleaseYear,
                maxReleaseYear);

            list.Add(new SanitizedResumeShippedTitleExtraction(
                GameName: game,
                StudioName: studio,
                Role: role,
                ReleaseYear: year));
        }

        return list;
    }

    private static bool TryGetArray(JsonElement obj, string propertyName, out JsonElement arrayElement)
    {
        if (obj.TryGetProperty(propertyName, out arrayElement) &&
            arrayElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        arrayElement = default;
        return false;
    }

    private static string? TryGetOptionalString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => null
        };
    }

    private static decimal? ParseHalfYearDecimal(
        JsonElement obj,
        string propertyName,
        decimal min,
        decimal max)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        decimal raw;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var num))
        {
            raw = num;
        }
        else if (prop.ValueKind == JsonValueKind.String &&
                 decimal.TryParse(prop.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            raw = parsed;
        }
        else
        {
            return null;
        }

        if (raw < min || raw > max)
        {
            return null;
        }

        // Snap to nearest 0.5-year increment required by CandidateProfileRules.
        var halfSteps = decimal.Round(raw * 2.0m, 0, MidpointRounding.AwayFromZero);
        var snapped = halfSteps / 2.0m;
        return Math.Clamp(snapped, min, max);
    }

    private static short? ParseBoundedYear(
        JsonElement obj,
        string propertyName,
        short minYear,
        short maxYear)
    {
        if (!obj.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        int year;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var num))
        {
            year = num;
        }
        else if (prop.ValueKind == JsonValueKind.String &&
                 int.TryParse(prop.GetString()?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            year = parsed;
        }
        else
        {
            return null;
        }

        if (year < minYear || year > maxYear)
        {
            return null;
        }

        return (short)year;
    }

    private static bool TryParseYearMonthOrYear(
        string? raw,
        int minYear,
        int maxYear,
        out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();

        // Format "YYYY-MM" or "YYYY/MM"
        if (trimmed.Length == 7 && (trimmed[4] == '-' || trimmed[4] == '/'))
        {
            if (int.TryParse(trimmed.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) &&
                int.TryParse(trimmed.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
                y >= minYear && y <= maxYear && m is >= 1 and <= 12)
            {
                date = new DateOnly(y, m, 1);
                return true;
            }
        }

        // Format "YYYY-MM-DD"
        if (trimmed.Length == 10 &&
            DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fullDate) &&
            fullDate.Year >= minYear && fullDate.Year <= maxYear)
        {
            date = new DateOnly(fullDate.Year, fullDate.Month, 1);
            return true;
        }

        // Format "YYYY"
        if (trimmed.Length == 4 &&
            int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var yearOnly) &&
            yearOnly >= minYear && yearOnly <= maxYear)
        {
            date = new DateOnly(yearOnly, 1, 1);
            return true;
        }

        return false;
    }

    private static bool IsPresentToken(string raw)
    {
        var t = raw.Trim();
        return t.Equals("present", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("current", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("now", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("null", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("ongoing", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TruncateClean(string? raw, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = CollapseWhitespace(StripRedactionPlaceholders(raw));
        if (cleaned.Length == 0)
        {
            return null;
        }

        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength].TrimEnd();
        }

        return cleaned.Length > 0 ? cleaned : null;
    }

    private static string StripRedactionPlaceholders(string input)
    {
        return input
            .Replace(ResumePiiRedactor.EmailPlaceholder, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(ResumePiiRedactor.UrlPlaceholder, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(ResumePiiRedactor.PhonePlaceholder, string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string input)
    {
        var parts = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : string.Join(' ', parts);
    }

    private static string UnwrapCodeFences(string raw)
    {
        if (!raw.StartsWith("```", StringComparison.Ordinal))
        {
            return raw;
        }

        var firstNewline = raw.IndexOf('\n');
        if (firstNewline < 0)
        {
            return raw;
        }

        var body = raw[(firstNewline + 1)..].Trim();
        if (body.EndsWith("```", StringComparison.Ordinal))
        {
            body = body[..^3].Trim();
        }

        return body;
    }
}
