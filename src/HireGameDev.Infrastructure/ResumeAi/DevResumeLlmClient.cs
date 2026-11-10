using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Infrastructure.ResumeAi;

/// <summary>
/// Deterministic offline <see cref="IResumeLlmClient"/> implementation for local development and automated tests (ADR-013).
/// Strictly follows the Blueprint extraction rule: extracts ONLY facts explicitly stated in the PII-redacted text,
/// never infers unmentioned skills, returns <c>null</c> for unknown fields, and emits the exact production JSON schema.
/// </summary>
internal sealed class DevResumeLlmClient : IResumeLlmClient
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex StatedYearsExperienceRegex = new(
        @"\b(?<years>\d{1,2}(?:\.\d)?)\s*\+?\s*(?:years?|yrs?)\s+(?:of\s+)?(?:professional\s+|industry\s+|game\s+dev(?:elopment)?\s+)?experience\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex DateRangeLineRegex = new(
        @"(?<start>(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+(?:19|20)\d{2}|(?:19|20)\d{2}[-/](?:0[1-9]|1[0-2])|(?:19|20)\d{2})\s*(?:[-–—]|to)\s*(?<end>Present|Current|Now|Ongoing|(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+(?:19|20)\d{2}|(?:19|20)\d{2}[-/](?:0[1-9]|1[0-2])|(?:19|20)\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex YearPairRegex = new(
        @"\b(?<start>(?:19|20)\d{2})\s*(?:[-–—]|to)\s*(?<end>(?:19|20)\d{2}|Present|Current|Expected)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex SingleYearRegex = new(
        @"\b(?<year>(?:199\d|20\d{2}))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex DegreeKeywordRegex = new(
        @"\b(?:B\.?\s*Tech|M\.?\s*Tech|B\.?\s*E\.?|M\.?\s*E\.?|B\.?\s*Sc|M\.?\s*Sc|BCA|MCA|B\.?\s*Des|M\.?\s*Des|BFA|MFA|Bachelor(?:'s)?|Master(?:'s)?|Diploma|PG\s*Diploma)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex RoleKeywordRegex = new(
        @"\b(?:Programmer|Developer|Engineer|Artist|Animator|Designer|Producer|Tester|QA|Director|Technical|Gameplay|Graphics|Shader|VFX|UI/UX|Audio|Sound|Composer|Lead|Architect)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private readonly IReferenceDataService _referenceDataService;
    private readonly IClock _clock;

    public DevResumeLlmClient(IReferenceDataService referenceDataService, IClock clock)
    {
        _referenceDataService = referenceDataService;
        _clock = clock;
    }

    public string ProviderName => "Dev";

    public async Task<Result<string>> ExtractResumeJsonAsync(
        string redactedResumeText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(redactedResumeText))
        {
            return Result.Failure<string>(
                Error.Validation("ResumeImport.EmptyRedactedText", "No readable resume text remained after PII redaction."));
        }

        var lines = SplitCleanLines(redactedResumeText);
        var sections = PartitionSections(lines);

        var headline = ExtractHeadline(sections.GetValueOrDefault(ResumeSection.Header) ?? []);
        var yearsExperience = ExtractStatedYearsExperience(redactedResumeText);

        var catalogSkills = await _referenceDataService.GetActiveSkillsAsync(cancellationToken: cancellationToken);
        var skills = ExtractStatedSkills(
            redactedResumeText,
            sections.GetValueOrDefault(ResumeSection.Skills) ?? [],
            catalogSkills);

        var experiences = ExtractExperiences(sections.GetValueOrDefault(ResumeSection.Experience) ?? []);
        var education = ExtractEducation(sections.GetValueOrDefault(ResumeSection.Education) ?? []);
        var shippedTitles = ExtractShippedTitles(sections.GetValueOrDefault(ResumeSection.ShippedTitles) ?? []);

        var jsonPayload = new
        {
            headline,
            years_experience = yearsExperience,
            skills,
            experiences,
            education,
            shipped_titles = shippedTitles
        };

        var json = JsonSerializer.Serialize(jsonPayload);
        return Result.Success(json);
    }

    private static List<string> SplitCleanLines(string text)
    {
        var rawLines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>(rawLines.Length);
        foreach (var raw in rawLines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                list.Add(trimmed);
            }
        }

        return list;
    }

    private static Dictionary<ResumeSection, List<string>> PartitionSections(List<string> lines)
    {
        var map = new Dictionary<ResumeSection, List<string>>
        {
            [ResumeSection.Header] = [],
            [ResumeSection.Summary] = [],
            [ResumeSection.Skills] = [],
            [ResumeSection.Experience] = [],
            [ResumeSection.Education] = [],
            [ResumeSection.ShippedTitles] = []
        };

        var current = ResumeSection.Header;
        foreach (var line in lines)
        {
            if (TryDetectSectionHeading(line, out var detected))
            {
                current = detected;
                continue;
            }

            map[current].Add(line);
        }

        return map;
    }

    private static bool TryDetectSectionHeading(string line, out ResumeSection section)
    {
        section = ResumeSection.Header;
        var clean = line.Trim().Trim(':', '-', '—', '–', '*', '#', '|').Trim();
        if (clean.Length is < 4 or > 36)
        {
            return false;
        }

        if (clean.Equals("Summary", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Professional Summary", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("About", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Profile", StringComparison.OrdinalIgnoreCase))
        {
            section = ResumeSection.Summary;
            return true;
        }

        if (clean.Equals("Skills", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Technical Skills", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Top Skills", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Core Skills", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Tools & Technologies", StringComparison.OrdinalIgnoreCase))
        {
            section = ResumeSection.Skills;
            return true;
        }

        if (clean.Equals("Experience", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Work Experience", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Professional Experience", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Employment History", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Work History", StringComparison.OrdinalIgnoreCase))
        {
            section = ResumeSection.Experience;
            return true;
        }

        if (clean.Equals("Education", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Academic Background", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Qualifications", StringComparison.OrdinalIgnoreCase))
        {
            section = ResumeSection.Education;
            return true;
        }

        if (clean.Equals("Shipped Titles", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Games Shipped", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Published Games", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Credits", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Game Credits", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Selected Projects", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Projects", StringComparison.OrdinalIgnoreCase))
        {
            section = ResumeSection.ShippedTitles;
            return true;
        }

        return false;
    }

    private static string? ExtractHeadline(List<string> headerLines)
    {
        // Skip line 0 (typically candidate's full name) and look for a role-bearing headline in the top 5 header lines.
        var maxIdx = Math.Min(headerLines.Count, 6);
        for (var i = 1; i < maxIdx; i++)
        {
            var candidate = StripPlaceholdersAndPipes(headerLines[i]);
            if (candidate.Length is >= 4 and <= 80 && RoleKeywordRegex.IsMatch(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static decimal? ExtractStatedYearsExperience(string text)
    {
        var match = StatedYearsExperienceRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        if (!decimal.TryParse(match.Groups["years"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var years))
        {
            return null;
        }

        if (years < CandidateProfileRules.MinYearsExperience || years > CandidateProfileRules.MaxYearsExperience)
        {
            return null;
        }

        return decimal.Round(years * 2.0m, 0, MidpointRounding.AwayFromZero) / 2.0m;
    }

    private static List<object> ExtractStatedSkills(
        string fullRedactedText,
        List<string> explicitSkillLines,
        IReadOnlyList<HireGameDev.Application.Features.ReferenceData.SkillLookupItem> catalogSkills)
    {
        var results = new List<object>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan full text with our zero-false-positive token matcher against the active skill catalog.
        foreach (var skill in catalogSkills)
        {
            if (results.Count >= CandidateProfileRules.MaxCandidateSkills)
            {
                break;
            }

            if (SkillEvidenceTextMatcher.ContainsAnySkillTerm(fullRedactedText, skill.Name, skill.Aliases) &&
                seenNames.Add(skill.Name))
            {
                results.Add(new
                {
                    name = skill.Name,
                    years_used = (decimal?)null,
                    last_used_year = (int?)null
                });
            }
        }

        // 2. Also surface explicit comma/pipe/bullet tokens listed inside a dedicated Skills section
        // so non-taxonomy tools appear as unmatched suggestions on the review screen.
        foreach (var rawLine in explicitSkillLines)
        {
            var line = rawLine;
            var colonIdx = line.IndexOf(':');
            if (colonIdx >= 0 && colonIdx < 28)
            {
                line = line[(colonIdx + 1)..];
            }

            var tokens = line.Split([',', '|', '•', '·', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var rawToken in tokens)
            {
                if (results.Count >= 35)
                {
                    break;
                }

                var token = rawToken.TrimStart('-', '*').Trim();
                if (token.Length is < 2 or > 40 ||
                    token.Contains('[') ||
                    token.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 4)
                {
                    continue;
                }

                // Skip if an alias of an already-added catalog skill matches this token.
                var alreadyCovered = catalogSkills.Any(cs =>
                    seenNames.Contains(cs.Name) &&
                    (string.Equals(cs.Name, token, StringComparison.OrdinalIgnoreCase) ||
                     cs.Aliases.Any(a => string.Equals(a, token, StringComparison.OrdinalIgnoreCase))));

                if (!alreadyCovered && seenNames.Add(token))
                {
                    results.Add(new
                    {
                        name = token,
                        years_used = (decimal?)null,
                        last_used_year = (int?)null
                    });
                }
            }
        }

        return results;
    }

    private List<object> ExtractExperiences(List<string> lines)
    {
        var results = new List<object>();
        if (lines.Count == 0)
        {
            return results;
        }

        var maxYear = _clock.UtcNow.Year + 1;

        for (var i = 0; i < lines.Count && results.Count < CandidateProfileRules.MaxCandidateExperiences; i++)
        {
            var dateMatch = DateRangeLineRegex.Match(lines[i]);
            if (!dateMatch.Success)
            {
                continue;
            }

            if (!TryNormalizeToYearMonth(dateMatch.Groups["start"].Value, maxYear, out var startYm))
            {
                continue;
            }

            var rawEndToken = dateMatch.Groups["end"].Value;
            string? endYm = null;
            if (!IsCurrentKeyword(rawEndToken) &&
                TryNormalizeToYearMonth(rawEndToken, maxYear, out var parsedEnd))
            {
                endYm = parsedEnd;
            }

            var headerRemainder = lines[i].Remove(dateMatch.Index, dateMatch.Length).Trim(' ', '|', '-', '–', '—', ',', '(', ')');
            var headerCandidate = headerRemainder.Length >= 4
                ? headerRemainder
                : (i > 0 ? lines[i - 1] : string.Empty);

            if (!TrySplitRoleAndCompany(headerCandidate, i > 1 ? lines[i - 2] : null, out var title, out var company))
            {
                continue;
            }

            var descLines = new List<string>();
            var k = i + 1;
            while (k < lines.Count && !DateRangeLineRegex.IsMatch(lines[k]))
            {
                // Stop if next line looks like the title/company header immediately preceding another date line.
                if (k + 1 < lines.Count && DateRangeLineRegex.IsMatch(lines[k + 1]))
                {
                    break;
                }

                var bullet = lines[k].TrimStart('•', '-', '*', '·').Trim();
                if (bullet.Length > 0)
                {
                    descLines.Add(bullet);
                }

                k++;
            }

            var description = descLines.Count == 0 ? null : string.Join("\n", descLines);
            results.Add(new
            {
                company,
                title,
                start = startYm,
                end = endYm,
                description
            });

            i = k - 1;
        }

        return results;
    }

    private static List<object> ExtractEducation(List<string> lines)
    {
        var results = new List<object>();
        if (lines.Count == 0)
        {
            return results;
        }

        for (var i = 0; i < lines.Count && results.Count < CandidateProfileRules.MaxCandidateEducationEntries; i++)
        {
            var line = lines[i];
            if (!DegreeKeywordRegex.IsMatch(line))
            {
                continue;
            }

            int? startYear = null;
            int? endYear = null;

            var yearPair = YearPairRegex.Match(line);
            if (!yearPair.Success && i + 1 < lines.Count)
            {
                yearPair = YearPairRegex.Match(lines[i + 1]);
            }

            if (yearPair.Success)
            {
                if (int.TryParse(yearPair.Groups["start"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sy))
                {
                    startYear = sy;
                }

                if (int.TryParse(yearPair.Groups["end"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ey))
                {
                    endYear = ey;
                }
            }
            else
            {
                var single = SingleYearRegex.Match(line);
                if (single.Success && int.TryParse(single.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var gradY))
                {
                    endYear = gradY;
                }
            }

            var cleanedLine = yearPair.Success && line.Contains(yearPair.Value, StringComparison.Ordinal)
                ? line.Replace(yearPair.Value, string.Empty, StringComparison.Ordinal)
                : line;

            cleanedLine = cleanedLine.Trim(' ', '|', '-', '–', '—', ',', '(', ')');
            string degree = cleanedLine;
            string? institution = null;
            string? field = null;

            var parts = cleanedLine.Split(["—", "–", "|", " - ", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                degree = parts[0];
                institution = parts[1];
            }
            else if (i > 0 && !DegreeKeywordRegex.IsMatch(lines[i - 1]))
            {
                institution = lines[i - 1].Trim();
            }
            else if (i + 1 < lines.Count && !DegreeKeywordRegex.IsMatch(lines[i + 1]) && !YearPairRegex.IsMatch(lines[i + 1]))
            {
                institution = lines[i + 1].Trim();
            }

            var inIdx = degree.IndexOf(" in ", StringComparison.OrdinalIgnoreCase);
            if (inIdx > 1 && inIdx + 4 < degree.Length)
            {
                field = degree[(inIdx + 4)..].Trim();
                degree = degree[..inIdx].Trim();
            }

            if (string.IsNullOrWhiteSpace(institution) || string.IsNullOrWhiteSpace(degree))
            {
                continue;
            }

            results.Add(new
            {
                institution,
                degree,
                field,
                start_year = startYear,
                end_year = endYear
            });
        }

        return results;
    }

    private static List<object> ExtractShippedTitles(List<string> lines)
    {
        var results = new List<object>();
        foreach (var raw in lines)
        {
            if (results.Count >= CandidateProfileRules.MaxShippedTitles)
            {
                break;
            }

            var line = raw.TrimStart('•', '-', '*', '·').Trim();
            if (line.Length < 4 || line.EndsWith('.'))
            {
                continue;
            }

            int? year = null;
            var yearMatch = SingleYearRegex.Match(line);
            if (yearMatch.Success && int.TryParse(yearMatch.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedY))
            {
                year = parsedY;
                line = line.Replace($"({yearMatch.Value})", string.Empty, StringComparison.Ordinal)
                           .Replace(yearMatch.Value, string.Empty, StringComparison.Ordinal)
                           .Trim(' ', '|', '-', '–', '—', ',');
            }

            var segments = line.Split(["—", "–", "|", " - "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0 || segments[0].Length is < 2 or > 100)
            {
                continue;
            }

            var game = segments[0];
            var role = segments.Length >= 2 ? segments[1] : null;
            var studio = segments.Length >= 3 ? segments[2] : null;

            results.Add(new
            {
                game,
                studio,
                role,
                year
            });
        }

        return results;
    }

    private static bool TrySplitRoleAndCompany(
        string primaryHeader,
        string? precedingLine,
        out string title,
        out string company)
    {
        title = string.Empty;
        company = string.Empty;

        var clean = StripPlaceholdersAndPipes(primaryHeader);
        if (clean.Length < 3)
        {
            return false;
        }

        var atIdx = clean.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (atIdx > 1 && atIdx + 4 < clean.Length)
        {
            title = clean[..atIdx].Trim();
            company = clean[(atIdx + 4)..].Trim();
            return title.Length > 0 && company.Length > 0;
        }

        var parts = primaryHeader.Split(["|", "—", "–", " - "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            if (RoleKeywordRegex.IsMatch(parts[0]))
            {
                title = parts[0];
                company = parts[1];
            }
            else
            {
                company = parts[0];
                title = parts[1];
            }

            return title.Length > 0 && company.Length > 0;
        }

        if (!string.IsNullOrWhiteSpace(precedingLine))
        {
            var prevClean = StripPlaceholdersAndPipes(precedingLine);
            if (prevClean.Length is >= 2 and <= 100 && !DateRangeLineRegex.IsMatch(prevClean))
            {
                if (RoleKeywordRegex.IsMatch(clean))
                {
                    title = clean;
                    company = prevClean;
                }
                else
                {
                    company = clean;
                    title = prevClean;
                }

                return true;
            }
        }

        return false;
    }

    private static bool TryNormalizeToYearMonth(string token, int maxYear, out string yearMonth)
    {
        yearMonth = string.Empty;
        var trimmed = token.Trim();

        if (trimmed.Length == 7 && (trimmed[4] == '-' || trimmed[4] == '/'))
        {
            if (int.TryParse(trimmed.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) &&
                int.TryParse(trimmed.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
                y >= 1970 && y <= maxYear && m is >= 1 and <= 12)
            {
                yearMonth = $"{y:D4}-{m:D2}";
                return true;
            }
        }

        if (trimmed.Length == 4 &&
            int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var yearOnly) &&
            yearOnly >= 1970 && yearOnly <= maxYear)
        {
            yearMonth = $"{yearOnly:D4}-01";
            return true;
        }

        var spaceParts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (spaceParts.Length == 2 &&
            TryParseMonthName(spaceParts[0], out var monthNum) &&
            int.TryParse(spaceParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var namedYear) &&
            namedYear >= 1970 && namedYear <= maxYear)
        {
            yearMonth = $"{namedYear:D4}-{monthNum:D2}";
            return true;
        }

        return false;
    }

    private static bool TryParseMonthName(string rawMonth, out int month)
    {
        var prefix = rawMonth.Length >= 3 ? rawMonth[..3].ToLowerInvariant() : rawMonth.ToLowerInvariant();
        month = prefix switch
        {
            "jan" => 1,
            "feb" => 2,
            "mar" => 3,
            "apr" => 4,
            "may" => 5,
            "jun" => 6,
            "jul" => 7,
            "aug" => 8,
            "sep" => 9,
            "oct" => 10,
            "nov" => 11,
            "dec" => 12,
            _ => 0
        };

        return month > 0;
    }

    private static bool IsCurrentKeyword(string token)
    {
        var t = token.Trim();
        return t.Equals("present", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("current", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("now", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("ongoing", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripPlaceholdersAndPipes(string input)
    {
        var cleaned = input
            .Replace(ResumePiiRedactor.EmailPlaceholder, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(ResumePiiRedactor.UrlPlaceholder, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(ResumePiiRedactor.PhonePlaceholder, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '|', '-', '–', '—', '•', ',');

        var parts = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : string.Join(' ', parts);
    }

    private enum ResumeSection
    {
        Header = 0,
        Summary = 1,
        Skills = 2,
        Experience = 3,
        Education = 4,
        ShippedTitles = 5
    }
}
