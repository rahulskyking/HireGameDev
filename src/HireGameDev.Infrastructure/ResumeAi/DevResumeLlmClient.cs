using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Infrastructure.ResumeAi;

/// <summary>
/// Deterministic offline <see cref="IResumeLlmClient"/> implementation for local development and API-key-free environments.
/// Understands LinkedIn "Save to PDF" multi-role blocks, 2-column resume sections, Indian city hubs,
/// game-dev role headlines, multi-line project/credit blocks, engine/platform mentions, and dated experience spans.
/// </summary>
internal sealed class DevResumeLlmClient : IResumeLlmClient
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex StatedYearsExperienceRegex = new(
        @"\b(?<years>\d{1,2}(?:\.\d)?)\s*\+?\s*(?:years?|yrs?)\s+(?:of\s+)?(?:professional\s+|total\s+|industry\s+|relevant\s+|hands[- ]on\s+|game\s+dev(?:elopment)?\s+)?experience\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex DateTokenCore = new(
        @"(?:(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s*'?(?:19|20)?\d{2}|(?:0?[1-9]|1[0-2])[-/](?:19|20)\d{2}|(?:19|20)\d{2}[-/](?:0?[1-9]|1[0-2])|(?:19|20)\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex DateRangeLineRegex = new(
        $@"(?<start>{DateTokenCore})\s*(?:[-–—~]|to)\s*(?<end>Present|Current|Now|Ongoing|Till\s+Date|{DateTokenCore})(?:\s*[·•|]?\s*\([^)]*(?:yr|year|mo|month)[^)]*\))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex LinkedInTenureOnlyRegex = new(
        @"^\s*\d+\s*(?:years?|yrs?)(?:\s+\d+\s*(?:months?|mos?))?\s*$|^\s*\d+\s*(?:months?|mos?)\s*$",
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
        @"\b(?:B\.?\s*Tech|M\.?\s*Tech|B\.?\s*E\.?|M\.?\s*E\.?|B\.?\s*Sc|M\.?\s*Sc|BCA|MCA|B\.?\s*Des|M\.?\s*Des|BFA|MFA|B\.?\s*A\.?|M\.?\s*A\.?|Bachelor(?:'s)?|Master(?:'s)?|PG\s*Diploma|Advanced\s+Diploma|Diploma|B\.?\s*Voc)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex InstitutionKeywordRegex = new(
        @"\b(?:University|Institute|College|IIT|NIT|IIIT|BITS|VIT|SRM|MIT|NID|Supinfocom|Rubika|Seamedu|Backstage\s+Pass|ICAT|Arena|MAAC|Academy|School\s+of)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex RoleKeywordRegex = new(
        @"\b(?:Programmer|Developer|Engineer|Artist|Animator|Rigger|Modeler|Designer|Producer|Tester|QA|Director|Technical|Gameplay|Graphics|Rendering|Shader|VFX|UI/UX|UI|UX|Audio|Sound|Composer|Writer|Narrative|Economy|Combat|Systems|Level|Environment|Character|Concept|Lighting|Lead|Senior|Junior|Principal|Architect|Intern|Associate|Coordinator|LiveOps|DevOps)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex EmploymentTypeSuffixRegex = new(
        @"\s*[·•|-]\s*(?:Full[- ]time|Part[- ]time|Contract|Freelance|Internship|Self[- ]employed)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex QuotedShippedGameInBulletRegex = new(
        @"(?:shipped|published|released|launched|credited\s+on|worked\s+on)\s+[""'“](?<game>[^""'”\r\n]{2,80})[""'”]",
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

        var headerLines = sections[ResumeSection.Header];
        var summaryLines = sections[ResumeSection.Summary];
        var skillLines = sections[ResumeSection.Skills];
        var expLines = sections[ResumeSection.Experience];
        var eduLines = sections[ResumeSection.Education];
        var titleLines = sections[ResumeSection.ShippedTitles];

        var catalogSkills = await _referenceDataService.GetActiveSkillsAsync(cancellationToken: cancellationToken);
        var engineSkills = catalogSkills.Where(s => s.Category == SkillCategory.Engine).ToList();
        var cities = await _referenceDataService.GetActiveCitiesAsync(cancellationToken);

        var fullName = ExtractFullName(headerLines);
        var experiences = ExtractExperiences(expLines, cities);
        var headline = ExtractHeadline(headerLines, experiences);
        var bio = ExtractBio(summaryLines, headerLines, fullName, headline);
        var city = ExtractCityName(headerLines, expLines, cities);
        var yearsExperience = ExtractStatedYearsExperience(redactedResumeText);

        var education = ExtractEducation(eduLines);
        var shippedTitles = ExtractShippedTitles(titleLines, experiences, engineSkills);

        var skills = ExtractStatedSkills(
            redactedResumeText,
            headline,
            skillLines,
            catalogSkills);

        var roleTitles = experiences.Select(e => e.title).ToList();
        var matchedCategories = skills
            .Select(s => catalogSkills.FirstOrDefault(cs => string.Equals(cs.Name, s.name, StringComparison.OrdinalIgnoreCase))?.Category)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .ToList();

        var inferredDiscipline = ResumeSignalEnricher.InferPrimaryDiscipline(headline, roleTitles, matchedCategories);

        var jsonPayload = new
        {
            full_name = fullName,
            headline,
            bio,
            primary_discipline = inferredDiscipline?.ToString().ToLowerInvariant(),
            city,
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
            [ResumeSection.ShippedTitles] = [],
            [ResumeSection.Other] = []
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
        var clean = line.Trim().Trim(':', '-', '—', '–', '*', '#', '|', '_').Trim();
        if (clean.Length is < 4 or > 42)
        {
            return false;
        }

        if (MatchesAnyHeading(clean,
            "Summary", "Professional Summary", "Executive Summary", "About", "About Me", "Profile", "Career Summary", "Objective"))
        {
            section = ResumeSection.Summary;
            return true;
        }

        if (MatchesAnyHeading(clean,
            "Skills", "Technical Skills", "Top Skills", "Core Skills", "Key Skills", "Core Competencies",
            "Tools & Technologies", "Technologies", "Tech Stack", "Skills & Tools"))
        {
            section = ResumeSection.Skills;
            return true;
        }

        if (MatchesAnyHeading(clean,
            "Experience", "Work Experience", "Professional Experience", "Industry Experience",
            "Employment History", "Work History", "Career History", "Relevant Experience"))
        {
            section = ResumeSection.Experience;
            return true;
        }

        if (MatchesAnyHeading(clean,
            "Education", "Academic Background", "Qualifications", "Academic Qualifications", "Education & Training"))
        {
            section = ResumeSection.Education;
            return true;
        }

        if (MatchesAnyHeading(clean,
            "Shipped Titles", "Games Shipped", "Published Games", "Released Games", "Commercial Titles",
            "Credits", "Game Credits", "Selected Projects", "Game Projects", "Personal Projects", "Projects", "Portfolio Projects"))
        {
            section = ResumeSection.ShippedTitles;
            return true;
        }

        if (MatchesAnyHeading(clean,
            "Contact", "Certifications", "Licenses & Certifications", "Languages",
            "Honors-Awards", "Honors & Awards", "Awards", "Achievements", "Interests", "References", "Publications"))
        {
            section = ResumeSection.Other;
            return true;
        }

        return false;
    }

    private static bool MatchesAnyHeading(string candidate, params string[] headings)
    {
        for (var i = 0; i < headings.Length; i++)
        {
            if (candidate.Equals(headings[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ExtractFullName(List<string> headerLines)
    {
        var maxIdx = Math.Min(headerLines.Count, 4);
        for (var i = 0; i < maxIdx; i++)
        {
            var candidate = StripPlaceholdersAndPipes(headerLines[i]);
            if (candidate.Length is < 3 or > 60)
            {
                continue;
            }

            if (RoleKeywordRegex.IsMatch(candidate) ||
                StatedYearsExperienceRegex.IsMatch(candidate) ||
                candidate.Any(char.IsDigit) ||
                candidate.Contains('@') ||
                candidate.Contains(':'))
            {
                continue;
            }

            var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is >= 2 and <= 5 && words.All(w => w.All(ch => char.IsLetter(ch) || ch is '.' or '\'' or '-')))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ExtractHeadline(
        List<string> headerLines,
        List<ExperienceJsonDto> experiences)
    {
        var maxIdx = Math.Min(headerLines.Count, 7);
        for (var i = 0; i < maxIdx; i++)
        {
            var candidate = StripPlaceholdersAndPipes(headerLines[i]);
            if (candidate.Length < 4 || !RoleKeywordRegex.IsMatch(candidate))
            {
                continue;
            }

            // Strip "| 5.0 years of experience" suffix if combined on the same header line so headline stays clean.
            var yearsMatch = StatedYearsExperienceRegex.Match(candidate);
            if (yearsMatch.Success && yearsMatch.Index > 4)
            {
                candidate = candidate[..yearsMatch.Index].Trim(' ', '|', '-', '–', '—', '•', ',');
            }

            if (candidate.Length > 80)
            {
                candidate = candidate[..80].TrimEnd();
            }

            if (candidate.Length >= 4)
            {
                return candidate;
            }
        }

        // Fallback to most recent experience job title when PDF has no explicit role subtitle under the name.
        if (experiences.Count > 0 && experiences[0].title.Length is >= 4 and <= 80)
        {
            return experiences[0].title;
        }

        return null;
    }

    private static string? ExtractBio(
        List<string> summaryLines,
        List<string> headerLines,
        string? fullName,
        string? headline)
    {
        if (summaryLines.Count > 0)
        {
            var joined = StripPlaceholdersAndPipes(string.Join(' ', summaryLines));
            if (joined.Length >= 25)
            {
                return joined.Length <= 1000 ? joined : joined[..1000].TrimEnd();
            }
        }

        // Some resumes put a 1-2 sentence intro paragraph directly in the header block without a "Summary" heading.
        var introSentences = new List<string>();
        foreach (var raw in headerLines)
        {
            var clean = StripPlaceholdersAndPipes(raw);
            if (clean.Length < 45)
            {
                continue;
            }

            if (string.Equals(clean, fullName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clean, headline, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            introSentences.Add(clean);
        }

        if (introSentences.Count > 0)
        {
            var combined = string.Join(' ', introSentences);
            return combined.Length <= 1000 ? combined : combined[..1000].TrimEnd();
        }

        return null;
    }

    private static string? ExtractCityName(
        List<string> headerLines,
        List<string> expLines,
        IReadOnlyList<CityLookupItem> cities)
    {
        if (cities.Count == 0)
        {
            return null;
        }

        var cityCatalog = cities
            .Select(c => new ResumeCityCatalogEntry(c.Id, c.Name, c.Slug, c.State, c.StateCode, c.Aliases, c.IsHub))
            .ToList();

        var headerText = string.Join('\n', headerLines);
        var matchedHeaderId = ResumeSignalEnricher.MatchIndianCityId(null, headerText, cityCatalog);
        if (matchedHeaderId.HasValue)
        {
            return cities.First(c => c.Id == matchedHeaderId.Value).Name;
        }

        var topExpText = string.Join('\n', expLines.Take(8));
        var matchedExpId = ResumeSignalEnricher.MatchIndianCityId(null, topExpText, cityCatalog);
        if (matchedExpId.HasValue)
        {
            return cities.First(c => c.Id == matchedExpId.Value).Name;
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

    private static List<SkillJsonDto> ExtractStatedSkills(
        string fullRedactedText,
        string? headline,
        List<string> explicitSkillLines,
        IReadOnlyList<SkillLookupItem> catalogSkills)
    {
        var results = new List<SkillJsonDto>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitSkillsBlob = string.Join('\n', explicitSkillLines);

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
                var inHeadline = !string.IsNullOrWhiteSpace(headline) &&
                                 SkillEvidenceTextMatcher.ContainsAnySkillTerm(headline, skill.Name, skill.Aliases);
                var inExplicitSection = explicitSkillsBlob.Length > 0 &&
                                        SkillEvidenceTextMatcher.ContainsAnySkillTerm(explicitSkillsBlob, skill.Name, skill.Aliases);

                results.Add(new SkillJsonDto(
                    name: skill.Name,
                    years_used: null,
                    last_used_year: null,
                    is_primary: inHeadline || inExplicitSection));
            }
        }

        // 2. Also surface explicit comma/pipe/bullet tokens listed inside a dedicated Skills / Top Skills section
        // so non-taxonomy tools appear as unmatched suggestions on the review screen.
        foreach (var rawLine in explicitSkillLines)
        {
            var line = rawLine;
            var colonIdx = line.IndexOf(':');
            if (colonIdx >= 0 && colonIdx < 32)
            {
                line = line[(colonIdx + 1)..];
            }

            var tokens = line.Split([',', '|', '•', '·', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var rawToken in tokens)
            {
                if (results.Count >= 38)
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

                var alreadyCovered = catalogSkills.Any(cs =>
                    seenNames.Contains(cs.Name) &&
                    (string.Equals(cs.Name, token, StringComparison.OrdinalIgnoreCase) ||
                     cs.Aliases.Any(a => string.Equals(a, token, StringComparison.OrdinalIgnoreCase))));

                if (!alreadyCovered && seenNames.Add(token))
                {
                    results.Add(new SkillJsonDto(
                        name: token,
                        years_used: null,
                        last_used_year: null,
                        is_primary: false));
                }
            }
        }

        return results;
    }

    private List<ExperienceJsonDto> ExtractExperiences(
        List<string> lines,
        IReadOnlyList<CityLookupItem> cities)
    {
        var results = new List<ExperienceJsonDto>();
        if (lines.Count == 0)
        {
            return results;
        }

        var maxYear = _clock.UtcNow.Year + 1;
        string? activeGroupCompany = null;

        for (var i = 0; i < lines.Count && results.Count < CandidateProfileRules.MaxCandidateExperiences; i++)
        {
            // Detect LinkedIn multi-role company header:
            // Line i: "Rockstar India"
            // Line i+1: "4 years 3 months"
            if (i + 1 < lines.Count &&
                LinkedInTenureOnlyRegex.IsMatch(lines[i + 1]) &&
                !DateRangeLineRegex.IsMatch(lines[i]))
            {
                var groupCandidate = StripPlaceholdersAndPipes(lines[i]);
                if (groupCandidate.Length is >= 2 and <= 120)
                {
                    activeGroupCompany = groupCandidate;
                    i++; // Skip the tenure-only line as well.
                    continue;
                }
            }

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

            var headerRemainder = lines[i].Remove(dateMatch.Index, dateMatch.Length)
                .Trim(' ', '|', '-', '–', '—', ',', '(', ')', '·', '•');
            headerRemainder = EmploymentTypeSuffixRegex.Replace(headerRemainder, string.Empty).Trim();

            var prevLine1 = i >= 1 && !LinkedInTenureOnlyRegex.IsMatch(lines[i - 1]) ? lines[i - 1] : null;
            var prevLine2 = i >= 2 && !LinkedInTenureOnlyRegex.IsMatch(lines[i - 2]) ? lines[i - 2] : null;

            var primaryCandidate = headerRemainder.Length >= 3
                ? headerRemainder
                : (prevLine1 ?? string.Empty);
            var secondaryCandidate = headerRemainder.Length >= 3
                ? prevLine1
                : prevLine2;

            if (!TryResolveRoleAndCompany(primaryCandidate, secondaryCandidate, activeGroupCompany, out var title, out var company))
            {
                continue;
            }

            // If this entry explicitly named a different company, update/reset activeGroupCompany.
            if (!string.IsNullOrWhiteSpace(activeGroupCompany) &&
                !string.Equals(company, activeGroupCompany, StringComparison.OrdinalIgnoreCase))
            {
                activeGroupCompany = null;
            }

            var descLines = new List<string>();
            var k = i + 1;
            while (k < lines.Count && !DateRangeLineRegex.IsMatch(lines[k]))
            {
                // Stop if next line is a LinkedIn multi-role company header ("Studio Name" followed by "X years Y months").
                if (k + 1 < lines.Count && LinkedInTenureOnlyRegex.IsMatch(lines[k + 1]))
                {
                    break;
                }

                // Stop if line k is the header (or 2-line Company + Role header) immediately preceding the next date line.
                if (k + 1 < lines.Count && DateRangeLineRegex.IsMatch(lines[k + 1]))
                {
                    var nextDateRem = lines[k + 1]
                        .Remove(DateRangeLineRegex.Match(lines[k + 1]).Index, DateRangeLineRegex.Match(lines[k + 1]).Length)
                        .Trim(' ', '|', '-', '–', '—', ',', '(', ')', '·', '•');
                    if (nextDateRem.Length < 3)
                    {
                        break;
                    }
                }

                if (k + 2 < lines.Count &&
                    DateRangeLineRegex.IsMatch(lines[k + 2]) &&
                    !lines[k].StartsWith('•') &&
                    !lines[k].StartsWith('-') &&
                    !lines[k].StartsWith('*') &&
                    lines[k].Length <= 70 &&
                    lines[k + 1].Length <= 70)
                {
                    var nextDateMatch2 = DateRangeLineRegex.Match(lines[k + 2]);
                    var nextRem2 = lines[k + 2].Remove(nextDateMatch2.Index, nextDateMatch2.Length).Trim();
                    if (nextRem2.Length < 4 && (RoleKeywordRegex.IsMatch(lines[k]) || RoleKeywordRegex.IsMatch(lines[k + 1])))
                    {
                        break;
                    }
                }

                // Skip standalone location line right beneath the date line (e.g. "Bengaluru, Karnataka, India" / "Remote").
                if (descLines.Count == 0 && k == i + 1 && IsStandaloneLocationLine(lines[k], cities))
                {
                    k++;
                    continue;
                }

                var bullet = lines[k].TrimStart('•', '-', '*', '·', '➢', '▪').Trim();
                if (bullet.Length > 0 && !LinkedInTenureOnlyRegex.IsMatch(bullet))
                {
                    descLines.Add(bullet);
                }

                k++;
            }

            var description = descLines.Count == 0 ? null : string.Join("\n", descLines);
            results.Add(new ExperienceJsonDto(
                company: company,
                title: title,
                start: startYm,
                end: endYm,
                description: description));

            i = k - 1;
        }

        return results;
    }

    private static bool IsStandaloneLocationLine(string line, IReadOnlyList<CityLookupItem> cities)
    {
        var trimmed = line.Trim();
        if (trimmed.Length is < 3 or > 55 ||
            trimmed.StartsWith('•') ||
            trimmed.StartsWith('-') ||
            trimmed.EndsWith('.'))
        {
            return false;
        }

        if (trimmed.Equals("Remote", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Hybrid", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("On-site", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(", India", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("Area, India", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.Contains(',') &&
            cities.Any(c => SkillEvidenceTextMatcher.ContainsAnySkillTerm(trimmed, c.Name, c.Aliases)))
        {
            return true;
        }

        return false;
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
                if (!single.Success && i + 1 < lines.Count)
                {
                    single = SingleYearRegex.Match(lines[i + 1]);
                }

                if (single.Success && int.TryParse(single.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var gradY))
                {
                    endYear = gradY;
                }
            }

            var cleanedLine = yearPair.Success && line.Contains(yearPair.Value, StringComparison.Ordinal)
                ? line.Replace(yearPair.Value, string.Empty, StringComparison.Ordinal)
                : line;

            cleanedLine = cleanedLine.Trim(' ', '|', '-', '–', '—', ',', '(', ')', '·', '•');
            string degree = cleanedLine;
            string? institution = null;
            string? field = null;

            var majorSeparators = cleanedLine.Split(["—", "–", "|", " - "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (majorSeparators.Length >= 2)
            {
                if (InstitutionKeywordRegex.IsMatch(majorSeparators[0]) && !InstitutionKeywordRegex.IsMatch(majorSeparators[1]))
                {
                    institution = majorSeparators[0];
                    degree = majorSeparators[1];
                }
                else
                {
                    degree = majorSeparators[0];
                    institution = majorSeparators[1];
                }
            }
            else if (i > 0 && !DegreeKeywordRegex.IsMatch(lines[i - 1]) && !YearPairRegex.IsMatch(lines[i - 1]))
            {
                institution = lines[i - 1].Trim(' ', '|', '-', '–', '—', ',', '·', '•');
            }
            else if (i + 1 < lines.Count && !DegreeKeywordRegex.IsMatch(lines[i + 1]) && !YearPairRegex.IsMatch(lines[i + 1]))
            {
                institution = lines[i + 1].Trim(' ', '|', '-', '–', '—', ',', '·', '•');
            }

            // LinkedIn format: "Bachelor of Technology - BTech, Computer Science" or "B.Tech in Computer Science"
            var inIdx = degree.IndexOf(" in ", StringComparison.OrdinalIgnoreCase);
            if (inIdx > 1 && inIdx + 4 < degree.Length)
            {
                field = degree[(inIdx + 4)..].Trim(' ', ',', '·', '(', ')');
                degree = degree[..inIdx].Trim();
            }
            else
            {
                var commaIdx = degree.IndexOf(',');
                if (commaIdx > 1 && commaIdx + 2 < degree.Length)
                {
                    var rightSide = degree[(commaIdx + 1)..].Trim(' ', '·', '(', ')');
                    if (!InstitutionKeywordRegex.IsMatch(rightSide))
                    {
                        field = rightSide;
                        degree = degree[..commaIdx].Trim();
                    }
                    else if (string.IsNullOrWhiteSpace(institution))
                    {
                        institution = rightSide;
                        degree = degree[..commaIdx].Trim();
                    }
                }
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

    private static List<object> ExtractShippedTitles(
        List<string> titleSectionLines,
        List<ExperienceJsonDto> experiences,
        IReadOnlyList<SkillLookupItem> engineSkills)
    {
        var results = new List<object>();
        var seenGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Parse multi-line blocks in Shipped Titles / Credits / Selected Projects section.
        for (var i = 0; i < titleSectionLines.Count && results.Count < CandidateProfileRules.MaxShippedTitles; i++)
        {
            var raw = titleSectionLines[i];
            var isBullet = raw.StartsWith('•') || raw.StartsWith('-') || raw.StartsWith('*') || raw.StartsWith('·');
            var line = raw.TrimStart('•', '-', '*', '·').Trim();

            // Skip narrative sentence lines unless it's a single-line bulleted title list without periods.
            if (line.Length < 3 || line.EndsWith('.'))
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

            var game = segments[0].Trim('"', '\'', '“', '”');
            if (game.Length < 2 || !seenGames.Add(game))
            {
                continue;
            }

            string? role = null;
            string? studio = null;
            for (var s = 1; s < segments.Length; s++)
            {
                var seg = segments[s];
                if (role is null && RoleKeywordRegex.IsMatch(seg))
                {
                    role = seg;
                }
                else if (studio is null)
                {
                    studio = seg;
                }
                else
                {
                    role ??= seg;
                }
            }

            // Gather optional indented/bulleted description lines immediately following this project header.
            var contextBuilder = new List<string> { raw };
            if (!isBullet)
            {
                var k = i + 1;
                while (k < titleSectionLines.Count)
                {
                    var next = titleSectionLines[k];
                    var nextIsBullet = next.StartsWith('•') || next.StartsWith('-') || next.StartsWith('*') || next.StartsWith('·');
                    if (!nextIsBullet && !next.EndsWith('.'))
                    {
                        break;
                    }

                    contextBuilder.Add(next);
                    k++;
                }

                i = k - 1;
            }

            var combinedContext = string.Join(' ', contextBuilder);
            var engineName = DetectEngineName(combinedContext, engineSkills);
            var platforms = ResumeSignalEnricher.NormalizeGamePlatforms(null, combinedContext);

            results.Add(new
            {
                game,
                studio,
                role,
                year,
                engine = engineName,
                platforms
            });
        }

        // 2. Scan Experience descriptions for explicit shipped/published quoted game titles!
        foreach (var exp in experiences)
        {
            if (results.Count >= CandidateProfileRules.MaxShippedTitles || string.IsNullOrWhiteSpace(exp.description))
            {
                continue;
            }

            var matches = QuotedShippedGameInBulletRegex.Matches(exp.description);
            foreach (Match m in matches)
            {
                if (results.Count >= CandidateProfileRules.MaxShippedTitles)
                {
                    break;
                }

                var game = m.Groups["game"].Value.Trim();
                if (game.Length is < 2 or > 100 || !seenGames.Add(game))
                {
                    continue;
                }

                int? expYear = null;
                if (!string.IsNullOrWhiteSpace(exp.end) && exp.end.Length >= 4 &&
                    int.TryParse(exp.end.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var endY))
                {
                    expYear = endY;
                }

                var engineName = DetectEngineName(exp.description, engineSkills);
                var platforms = ResumeSignalEnricher.NormalizeGamePlatforms(null, exp.description);

                results.Add(new
                {
                    game,
                    studio = exp.company,
                    role = exp.title,
                    year = expYear,
                    engine = engineName,
                    platforms
                });
            }
        }

        return results;
    }

    private static string? DetectEngineName(string text, IReadOnlyList<SkillLookupItem> engineSkills)
    {
        foreach (var eng in engineSkills)
        {
            if (SkillEvidenceTextMatcher.ContainsAnySkillTerm(text, eng.Name, eng.Aliases))
            {
                return eng.Name;
            }
        }

        return null;
    }

    private static bool TryResolveRoleAndCompany(
        string primaryHeader,
        string? secondaryHeader,
        string? activeGroupCompany,
        out string title,
        out string company)
    {
        title = string.Empty;
        company = string.Empty;

        var clean = EmploymentTypeSuffixRegex.Replace(StripPlaceholdersAndPipes(primaryHeader), string.Empty).Trim();
        if (clean.Length < 2)
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

        var parts = clean.Split(["|", "—", "–", " - "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            if (RoleKeywordRegex.IsMatch(parts[0]) && !RoleKeywordRegex.IsMatch(parts[1]))
            {
                title = parts[0];
                company = parts[1];
            }
            else if (!RoleKeywordRegex.IsMatch(parts[0]) && RoleKeywordRegex.IsMatch(parts[1]))
            {
                company = parts[0];
                title = parts[1];
            }
            else
            {
                title = parts[0];
                company = parts[1];
            }

            return title.Length > 0 && company.Length > 0;
        }

        if (!string.IsNullOrWhiteSpace(secondaryHeader))
        {
            var prevClean = EmploymentTypeSuffixRegex.Replace(StripPlaceholdersAndPipes(secondaryHeader), string.Empty).Trim();
            if (prevClean.Length is >= 2 and <= 100 &&
                !DateRangeLineRegex.IsMatch(prevClean) &&
                !LinkedInTenureOnlyRegex.IsMatch(prevClean) &&
                !prevClean.StartsWith('•') &&
                !prevClean.StartsWith('-') &&
                !prevClean.EndsWith('.'))
            {
                if (RoleKeywordRegex.IsMatch(clean) && !RoleKeywordRegex.IsMatch(prevClean))
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

        // LinkedIn grouped multi-role fallback: role line followed by date line under an active studio group!
        if (!string.IsNullOrWhiteSpace(activeGroupCompany) && RoleKeywordRegex.IsMatch(clean))
        {
            title = clean;
            company = activeGroupCompany;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeToYearMonth(string token, int maxYear, out string yearMonth)
    {
        yearMonth = string.Empty;
        var trimmed = token.Trim();

        // "YYYY-MM" or "YYYY/MM" or "YYYY-M"
        if (trimmed.Length is 6 or 7 && (trimmed[4] == '-' || trimmed[4] == '/'))
        {
            if (int.TryParse(trimmed.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) &&
                int.TryParse(trimmed.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
                y >= 1970 && y <= maxYear && m is >= 1 and <= 12)
            {
                yearMonth = $"{y:D4}-{m:D2}";
                return true;
            }
        }

        // "MM/YYYY" or "M/YYYY" or "MM-YYYY"
        if (trimmed.Length is 6 or 7 && (trimmed.Contains('/') || trimmed.Contains('-')))
        {
            var sep = trimmed.Contains('/') ? '/' : '-';
            var parts = trimmed.Split(sep, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var m2) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y2) &&
                y2 >= 1970 && y2 <= maxYear && m2 is >= 1 and <= 12)
            {
                yearMonth = $"{y2:D4}-{m2:D2}";
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
            TryParseMonthName(spaceParts[0], out var monthNum))
        {
            var rawYearPart = spaceParts[1].TrimStart('\'');
            if (rawYearPart.Length == 2 &&
                int.TryParse(rawYearPart, NumberStyles.None, CultureInfo.InvariantCulture, out var shortY))
            {
                var fullShortY = shortY <= (maxYear % 100) + 1 ? 2000 + shortY : 1900 + shortY;
                yearMonth = $"{fullShortY:D4}-{monthNum:D2}";
                return true;
            }

            if (int.TryParse(rawYearPart, NumberStyles.None, CultureInfo.InvariantCulture, out var namedYear) &&
                namedYear >= 1970 && namedYear <= maxYear)
            {
                yearMonth = $"{namedYear:D4}-{monthNum:D2}";
                return true;
            }
        }

        return false;
    }

    private static bool TryParseMonthName(string rawMonth, out int month)
    {
        var clean = rawMonth.TrimEnd('.', ',');
        var prefix = clean.Length >= 3 ? clean[..3].ToLowerInvariant() : clean.ToLowerInvariant();
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
               t.Equals("ongoing", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("till date", StringComparison.OrdinalIgnoreCase);
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

    private sealed record SkillJsonDto(
        string name,
        decimal? years_used,
        int? last_used_year,
        bool is_primary);

    private sealed record ExperienceJsonDto(
        string company,
        string title,
        string start,
        string? end,
        string? description);

    private enum ResumeSection
    {
        Header = 0,
        Summary = 1,
        Skills = 2,
        Experience = 3,
        Education = 4,
        ShippedTitles = 5,
        Other = 6
    }
}
