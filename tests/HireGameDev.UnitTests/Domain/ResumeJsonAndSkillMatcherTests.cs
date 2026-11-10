using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class ResumeJsonAndSkillMatcherTests
{
    private static readonly IReadOnlyList<ResumeCatalogSkillEntry> SampleCatalog =
    [
        new(1, "Unity", "unity", SkillCategory.Engine, ["Unity3D", "Unity Engine"]),
        new(2, "Unreal Engine", "unreal-engine", SkillCategory.Engine, ["UE4", "UE5", "Unreal"]),
        new(3, "C#", "csharp", SkillCategory.Language, ["CSharp"]),
        new(4, "C++", "cpp", SkillCategory.Language, ["CPP"]),
        new(5, "C", "c", SkillCategory.Language, []),
        new(6, "Go", "go", SkillCategory.Language, ["Golang"]),
        new(7, "Node.js", "nodejs", SkillCategory.Tool, ["Node"]),
        new(8, "Blender", "blender", SkillCategory.DccTool, ["Blender 3D"])
    ];

    [Fact]
    public void TryParseAndSanitize_HandlesCodeFencedJson_SnapsHalfYears_AndFiltersOutOfBoundsValues()
    {
        const string fencedJson =
            """
            ```json
            {
              "headline": "Senior Gameplay Programmer building combat & multiplayer systems across PC and consoles with extra text beyond eighty chars",
              "years_experience": 4.4,
              "skills": [
                {"name": "Unity", "years_used": 4.2, "last_used_year": 2026},
                {"name": "C#", "years_used": 99.0, "last_used_year": 1800},
                {"name": "", "years_used": 1.0, "last_used_year": 2025}
              ],
              "experiences": [
                {
                  "company": "SuperGaming",
                  "title": "Senior Gameplay Programmer",
                  "start": "2022-03",
                  "end": null,
                  "description": "Built networked combat systems in Unity and C#."
                },
                {
                  "company": "Missing Start Co",
                  "title": "Intern",
                  "start": "invalid-date",
                  "end": "2021-05",
                  "description": "Ignored because start date is invalid."
                }
              ],
              "education": [
                {
                  "institution": "NIT Trichy",
                  "degree": "B.Tech",
                  "field": "Computer Science",
                  "start_year": 2016,
                  "end_year": 2020
                }
              ],
              "shipped_titles": [
                {
                  "game": "Indus Battle Royale",
                  "studio": "SuperGaming",
                  "role": "Gameplay Programmer",
                  "year": 2024
                }
              ]
            }
            ```
            """;

        var ok = ResumeExtractionJsonParser.TryParseAndSanitize(fencedJson, 2026, out var payload, out var error);

        Assert.True(ok, error);
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Headline);
        Assert.True(payload.Headline!.Length <= 80);
        Assert.Equal(4.5m, payload.YearsExperience);

        Assert.Equal(2, payload.Skills.Count);
        Assert.Equal("Unity", payload.Skills[0].Name);
        Assert.Equal(4.0m, payload.Skills[0].YearsUsed);
        Assert.Equal((short)2026, payload.Skills[0].LastUsedYear);

        // Out-of-range years_used (99.0) and last_used_year (1800) are safely nulled rather than crashing.
        Assert.Equal("C#", payload.Skills[1].Name);
        Assert.Null(payload.Skills[1].YearsUsed);
        Assert.Null(payload.Skills[1].LastUsedYear);

        Assert.Single(payload.Experiences);
        Assert.Equal("SuperGaming", payload.Experiences[0].CompanyName);
        Assert.Equal(new DateOnly(2022, 3, 1), payload.Experiences[0].StartDate);
        Assert.True(payload.Experiences[0].IsCurrent);
        Assert.Null(payload.Experiences[0].EndDate);

        Assert.Single(payload.Education);
        Assert.Equal((short)2016, payload.Education[0].StartYear);
        Assert.Equal((short)2020, payload.Education[0].EndYear);

        Assert.Single(payload.ShippedTitles);
        Assert.Equal("Indus Battle Royale", payload.ShippedTitles[0].GameName);
    }

    [Fact]
    public void TryParseAndSanitize_RejectsMalformedOrArrayRootJson()
    {
        Assert.False(ResumeExtractionJsonParser.TryParseAndSanitize("[1, 2, 3]", 2026, out _, out var arrErr));
        Assert.Contains("object", arrErr, StringComparison.OrdinalIgnoreCase);

        Assert.False(ResumeExtractionJsonParser.TryParseAndSanitize("{broken json", 2026, out _, out var badErr));
        Assert.Contains("invalid JSON", badErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MatchSingle_MatchesExactAliasesCompactAndTrigramTypos_WithoutShortSymbolCollisions()
    {
        // Exact alias ("UE5" -> "Unreal Engine")
        var ueMatch = ResumeSkillTaxonomyMatcher.MatchSingle("UE5", SampleCatalog);
        Assert.NotNull(ueMatch);
        Assert.Equal(2, ueMatch!.SkillId);
        Assert.Equal(SkillTaxonomyMatchKind.ExactNameOrAlias, ueMatch.MatchKind);

        // Compact punctuation-normalized ("NodeJS" -> "Node.js")
        var nodeMatch = ResumeSkillTaxonomyMatcher.MatchSingle("NodeJS", SampleCatalog);
        Assert.NotNull(nodeMatch);
        Assert.Equal(7, nodeMatch!.SkillId);
        Assert.Equal(SkillTaxonomyMatchKind.NormalizedToken, nodeMatch.MatchKind);

        // Trigram similarity ("Unrel Engine" typo -> "Unreal Engine")
        var typoMatch = ResumeSkillTaxonomyMatcher.MatchSingle("Unrel Engine", SampleCatalog);
        Assert.NotNull(typoMatch);
        Assert.Equal(2, typoMatch!.SkillId);
        Assert.Equal(SkillTaxonomyMatchKind.TrigramSimilarity, typoMatch.MatchKind);

        // Version suffix ("Blender (4.1)" -> "Blender")
        var blenderMatch = ResumeSkillTaxonomyMatcher.MatchSingle("Blender (4.1)", SampleCatalog);
        Assert.NotNull(blenderMatch);
        Assert.Equal(8, blenderMatch!.SkillId);

        // C, C#, and C++ must never collide with one another!
        Assert.Equal(5, ResumeSkillTaxonomyMatcher.MatchSingle("C", SampleCatalog)!.SkillId);
        Assert.Equal(3, ResumeSkillTaxonomyMatcher.MatchSingle("C#", SampleCatalog)!.SkillId);
        Assert.Equal(4, ResumeSkillTaxonomyMatcher.MatchSingle("C++", SampleCatalog)!.SkillId);

        // Unmatched custom tool returns null so caller surfaces it in UnmatchedSkillSuggestions.
        Assert.Null(ResumeSkillTaxonomyMatcher.MatchSingle("ProprietaryVoxelTool", SampleCatalog));
        Assert.Null(ResumeSkillTaxonomyMatcher.MatchSingle("CSS", SampleCatalog));
    }
}
