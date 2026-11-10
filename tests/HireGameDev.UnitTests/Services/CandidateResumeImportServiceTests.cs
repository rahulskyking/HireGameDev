using System.Net;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.ResumeAi;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class CandidateResumeImportServiceTests
{
    [Fact]
    public async Task ParseResumePdfAsync_RejectsScannedOrShortPdf_LogsRejectedStatus_AndPreservesDailyQuota()
    {
        await using var harness = await ResumeImportTestHarness.CreateAsync();

        await using var shortPdf = CreatePdfStream(["Arjun Rao", "Unity Dev"]);
        var result = await harness.CreateService().ParseResumePdfAsync(new ParseCandidateResumeCommand(
            UserId: harness.UserId,
            PdfStream: shortPdf,
            FileName: "scanned-resume.pdf"));

        Assert.True(result.IsFailure);
        Assert.Equal("ResumeImport.ScannedOrTooShort", result.Error.Code);

        var log = await harness.DbContext.ResumeParseLogs.SingleAsync();
        Assert.Equal(ResumeParseStatus.Rejected, log.Status);
        Assert.True(log.InputChars < ResumePdfUploadRules.MinExtractedTextChars);

        // Rejected scanned PDF must not burn the candidate's 5/day LLM parse quota.
        var quota = await harness.CreateService().GetQuotaStatusAsync(harness.UserId);
        Assert.Equal(5, quota.RemainingToday);
    }

    [Fact]
    public async Task ParseResumePdfAsync_RedactsAllPiiBeforeInvokingLlmClient_AndLogsMetadataOnly()
    {
        await using var harness = await ResumeImportTestHarness.CreateAsync();
        var spyLlm = new SpyResumeLlmClient();
        var service = harness.CreateService(spyLlm);

        await using var pdf = CreatePdfStream(
        [
            "Karan Malhotra",
            "Senior Gameplay Programmer | 5.0 years of experience",
            "Email: karan.malhotra@gmail.com | Mobile: +91 98201 44556 | Portfolio: https://karan.dev/games",
            "LinkedIn: linkedin.com/in/karanmalhotra | GitHub: github.com/karanmalhotra",
            "SUMMARY",
            "Experienced game programmer specializing in real-time combat, AI state machines, and multiplayer replication in Unity and C#.",
            "EXPERIENCE",
            "Gameplay Programmer | Nodding Heads Games | 2021-04 - Present",
            "- Engineered responsive melee combat systems and boss AI behaviors in Unity and C# for PC and console platforms."
        ]);

        var parseResult = await service.ParseResumePdfAsync(new ParseCandidateResumeCommand(
            UserId: harness.UserId,
            PdfStream: pdf,
            FileName: "karan-resume.pdf"));

        Assert.True(parseResult.IsSuccess, parseResult.Error.Message);
        Assert.NotNull(spyLlm.LastReceivedPromptText);

        // Verify zero raw PII reached IResumeLlmClient!
        Assert.DoesNotContain("karan.malhotra@gmail.com", spyLlm.LastReceivedPromptText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("98201", spyLlm.LastReceivedPromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("karan.dev", spyLlm.LastReceivedPromptText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("linkedin.com/in/karanmalhotra", spyLlm.LastReceivedPromptText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github.com/karanmalhotra", spyLlm.LastReceivedPromptText, StringComparison.OrdinalIgnoreCase);

        var log = await harness.DbContext.ResumeParseLogs.SingleAsync();
        Assert.Equal(ResumeParseStatus.Ok, log.Status);
        Assert.True(log.InputChars >= 200);
        Assert.NotNull(log.DurationMs);
        Assert.Equal(4, parseResult.Value.QuotaAfterParse.RemainingToday);
    }

    [Fact]
    public async Task EndToEnd_DevResumeLlmClient_ExtractsDraft_AndApplyMergesProfileWithBackedEvidence()
    {
        await using var harness = await ResumeImportTestHarness.CreateAsync();
        var service = harness.CreateService();

        await using var pdf = CreatePdfStream(
        [
            "Ananya Iyer",
            "Gameplay Programmer",
            "4.0 years of experience building cross-platform action games across PC and mobile.",
            "Email: ananya@example.com | Phone: +91 98450 12345 | github.com/ananyaiyer",
            "SKILLS",
            "Unity, C#, C++, CustomFrameProfiler",
            "EXPERIENCE",
            "Gameplay Programmer | Lucid Labs | 2021-06 - Present",
            "- Built character locomotion, shader-driven VFX hooks, and networked gameplay systems using Unity and C#.",
            "EDUCATION",
            "B.Tech in Computer Science — VIT Vellore | 2017 - 2021",
            "SHIPPED TITLES",
            "Possessions (2022) — Gameplay Programmer — Lucid Labs"
        ]);

        var draftResult = await service.ParseResumePdfAsync(new ParseCandidateResumeCommand(
            UserId: harness.UserId,
            PdfStream: pdf,
            FileName: "ananya-resume.pdf"));

        Assert.True(draftResult.IsSuccess, draftResult.Error.Message);
        var draft = draftResult.Value;

        Assert.Equal("Gameplay Programmer", draft.Headline);
        Assert.Equal(4.0m, draft.YearsExperience);
        Assert.Contains(draft.MatchedSkills, s => s.SkillName == "Unity");
        Assert.Contains(draft.MatchedSkills, s => s.SkillName == "C#");
        Assert.Contains(draft.MatchedSkills, s => s.SkillName == "C++");
        Assert.Contains("CustomFrameProfiler", draft.UnmatchedSkillSuggestions);
        Assert.Single(draft.Experiences);
        Assert.Single(draft.Education);
        Assert.Single(draft.ShippedTitles);

        // Confirm & apply draft to CandidateProfile!
        var applyResult = await service.ApplyImportedDraftAsync(new ApplyCandidateResumeDraftCommand(
            UserId: harness.UserId,
            ApplyHeadline: true,
            Headline: draft.Headline,
            ApplyYearsExperience: true,
            YearsExperience: draft.YearsExperience,
            Skills: draft.MatchedSkills
                .Select((s, idx) => new ConfirmImportedSkillInput(
                    SkillId: s.SkillId,
                    Proficiency: s.SuggestedProficiency,
                    YearsUsed: s.YearsUsed,
                    LastUsedYear: s.LastUsedYear,
                    IsPrimary: idx < 2))
                .ToArray(),
            Experiences: draft.Experiences
                .Select(e => new ConfirmImportedExperienceInput(
                    e.CompanyName,
                    e.Title,
                    e.StartDate,
                    e.EndDate,
                    e.IsCurrent,
                    e.Description))
                .ToArray(),
            Education: draft.Education
                .Select(ed => new ConfirmImportedEducationInput(
                    ed.Institution,
                    ed.Degree,
                    ed.Field,
                    ed.StartYear,
                    ed.EndYear))
                .ToArray(),
            ShippedTitles: draft.ShippedTitles
                .Select(st => new ConfirmImportedShippedTitleInput(
                    GameName: st.GameName,
                    StudioName: st.StudioName ?? "Lucid Labs",
                    Role: st.Role ?? "Gameplay Programmer",
                    Platforms: ["pc", "ios"],
                    EngineSkillId: 1, // Unity
                    ReleaseYear: st.ReleaseYear))
                .ToArray()));

        Assert.True(applyResult.IsSuccess, applyResult.Error.Message);
        Assert.Equal(3, applyResult.Value.AddedSkillCount);
        Assert.Equal(1, applyResult.Value.AddedExperienceCount);
        Assert.Equal(1, applyResult.Value.AddedEducationCount);
        Assert.Equal(1, applyResult.Value.AddedShippedTitleCount);

        // Unity and C# are mentioned in Experience description / ShippedTitle engine -> upgraded to Backed!
        var savedSkills = await harness.DbContext.CandidateSkills
            .AsNoTracking()
            .Where(cs => cs.CandidateId == harness.CandidateId)
            .ToDictionaryAsync(cs => cs.SkillId);

        Assert.Equal(SkillEvidenceLevel.Backed, savedSkills[1].EvidenceLevel); // Unity
        Assert.Equal(SkillEvidenceLevel.Backed, savedSkills[2].EvidenceLevel); // C#
        Assert.Equal(SkillEvidenceLevel.Claimed, savedSkills[3].EvidenceLevel); // C++ (not in exp/shipped proof)

        // Imported shipped title must strictly start with Verification = None.
        var savedTitle = await harness.DbContext.ShippedTitles.SingleAsync(t => t.CandidateId == harness.CandidateId);
        Assert.Equal(ShippedTitleVerification.None, savedTitle.Verification);
    }

    [Fact]
    public async Task ParseResumePdfAsync_EnforcesFivePerUtcDayQuota_AndResetsNextUtcDay()
    {
        await using var harness = await ResumeImportTestHarness.CreateAsync();
        var spyLlm = new SpyResumeLlmClient();
        var service = harness.CreateService(spyLlm);

        for (var i = 1; i <= 5; i++)
        {
            await using var pdf = CreatePdfStream(
            [
                "Dev Candidate",
                "Senior Unity Gameplay Programmer with 5.0 years of experience building action RPGs for PC and consoles in Bengaluru.",
                "Built deterministic combat state machines, AI behavior trees, and custom editor tooling in Unity and C# across multiple commercial game launches."
            ]);

            var ok = await service.ParseResumePdfAsync(new ParseCandidateResumeCommand(harness.UserId, pdf, $"r{i}.pdf"));
            Assert.True(ok.IsSuccess, ok.Error.Message);
            Assert.Equal(5 - i, ok.Value.QuotaAfterParse.RemainingToday);
        }

        // 6th attempt on the same UTC day must be rejected!
        await using var sixthPdf = CreatePdfStream(
        [
            "Dev Candidate",
            "Senior Unity Gameplay Programmer with 5.0 years of experience building action RPGs for PC and consoles in Bengaluru.",
            "Built deterministic combat state machines, AI behavior trees, and custom editor tooling in Unity and C# across multiple commercial game launches."
        ]);

        var sixthResult = await service.ParseResumePdfAsync(new ParseCandidateResumeCommand(harness.UserId, sixthPdf, "r6.pdf"));
        Assert.True(sixthResult.IsFailure);
        Assert.Equal("ResumeImport.DailyQuotaExceeded", sixthResult.Error.Code);

        // Advance clock by 1 UTC day -> quota resets!
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddDays(1);
        var nextDayQuota = await service.GetQuotaStatusAsync(harness.UserId);
        Assert.Equal(5, nextDayQuota.RemainingToday);
    }

    [Fact]
    public async Task ParseResumePdfAsync_HandlesTwoColumnLinkedInMultiRolePdf_AndInfersDatesEnginesPlatformsDisciplineAndCity()
    {
        await using var harness = await ResumeImportTestHarness.CreateAsync();
        var service = harness.CreateService();

        await using var linkedInPdf = CreateTwoColumnLinkedInPdfStream(
            leftSidebarLines:
            [
                "Contact",
                "rohan.art@gmail.com",
                "www.linkedin.com/in/rohankulkarni (LinkedIn)",
                "Top Skills",
                "Blender",
                "Unity"
            ],
            rightMainLines:
            [
                "Rohan Kulkarni",
                "3D Environment Artist",
                "Bangalore, Karnataka, India",
                "Summary",
                "3D Environment Artist crafting modular worlds, trim sheets, and optimized PBR assets in Blender and Unity.",
                "Experience",
                "Rockstar India",
                "3 years 6 months",
                "Senior Environment Artist",
                "June 2024 - Present (2 years 5 months)",
                "Bangalore, Karnataka, India",
                "• Built modular architectural kits and shader setups in Blender and Unity; shipped \"Red Dead Online\" updates for PC, PS5, and Xbox.",
                "Environment Artist",
                "May 2023 - May 2024 (1 year 1 month)",
                "Bangalore, Karnataka, India",
                "• Modeled hero props and foliage assets in Blender.",
                "Education",
                "MIT Institute of Design",
                "B.Des, Game Art & Design",
                "2019 - 2023",
                "Page 1 of 1"
            ]);

        var result = await service.ParseResumePdfAsync(new ParseCandidateResumeCommand(
            UserId: harness.UserId,
            PdfStream: linkedInPdf,
            FileName: "Profile.pdf"));

        Assert.True(result.IsSuccess, result.Error.Message);
        var draft = result.Value;

        Assert.Equal(ResumeParseSource.LinkedinPdf, draft.Source);
        Assert.Equal("Rohan Kulkarni", draft.SuggestedFullName);
        Assert.Equal("3D Environment Artist", draft.Headline);
        Assert.Equal(DisciplineCategory.Art, draft.SuggestedDiscipline);
        Assert.Equal(1, draft.SuggestedCityId); // "Bangalore" -> canonical Bengaluru (Id = 1)
        Assert.NotNull(draft.Bio);

        // Merged May 2023 -> Oct 2026 = 42 months = 3.5 years (computed automatically without "X years of experience" text!)
        Assert.Equal(3.5m, draft.YearsExperience);

        // Both sub-roles under the LinkedIn multi-role block must resolve CompanyName = "Rockstar India" (never "3 years 6 months"!)
        Assert.Equal(2, draft.Experiences.Count);
        Assert.All(draft.Experiences, e => Assert.Equal("Rockstar India", e.CompanyName));
        Assert.DoesNotContain(draft.Experiences, e => (e.Description ?? string.Empty).Contains("Karnataka, India", StringComparison.OrdinalIgnoreCase));

        // Per-skill YearsUsed and LastUsedYear inferred from dated roles!
        var blenderSkill = Assert.Single(draft.MatchedSkills, s => s.SkillName == "Blender");
        Assert.Equal(3.5m, blenderSkill.YearsUsed);
        Assert.Equal((short)2026, blenderSkill.LastUsedYear);
        Assert.True(blenderSkill.SuggestedPrimary);

        // Education parsed from LinkedIn 3-line block!
        var edu = Assert.Single(draft.Education);
        Assert.Equal("MIT Institute of Design", edu.Institution);
        Assert.Equal("B.Des", edu.Degree);
        Assert.Equal((short)2019, edu.StartYear);
        Assert.Equal((short)2023, edu.EndYear);

        // Quoted shipped game inside Experience bullet extracted with matched Unity engine & PC/Console platforms!
        var shipped = Assert.Single(draft.ShippedTitles);
        Assert.Equal("Red Dead Online", shipped.GameName);
        Assert.Equal("Rockstar India", shipped.StudioName);
        Assert.Equal(1, shipped.MatchedEngineSkillId); // Unity
        Assert.NotNull(shipped.Platforms);
        Assert.Contains("pc", shipped.Platforms!);
        Assert.Contains("console", shipped.Platforms!);
    }

    private static MemoryStream CreateTwoColumnLinkedInPdfStream(
        IReadOnlyList<string> leftSidebarLines,
        IReadOnlyList<string> rightMainLines)
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var leftY = 790.0;
        foreach (var line in leftSidebarLines)
        {
            page.AddText(line, 9.5, new PdfPoint(25, leftY), font);
            leftY -= 16.0;
        }

        var rightY = 790.0;
        foreach (var line in rightMainLines)
        {
            page.AddText(line, 10.5, new PdfPoint(225, rightY), font);
            rightY -= 16.0;
        }

        var bytes = builder.Build();
        return new MemoryStream(bytes, writable: false);
    }

    private static MemoryStream CreatePdfStream(IReadOnlyList<string> lines)
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var y = 780.0;
        foreach (var line in lines)
        {
            page.AddText(line, 11, new PdfPoint(40, y), font);
            y -= 18.0;
        }

        var bytes = builder.Build();
        return new MemoryStream(bytes, writable: false);
    }

    private sealed class SpyResumeLlmClient : IResumeLlmClient
    {
        public string ProviderName => "SpyTestLlm";

        public string? LastReceivedPromptText { get; private set; }

        public Task<Result<string>> ExtractResumeJsonAsync(
            string redactedResumeText,
            CancellationToken cancellationToken = default)
        {
            LastReceivedPromptText = redactedResumeText;
            const string json =
                """
                {
                  "headline": "Senior Gameplay Programmer",
                  "years_experience": 5.0,
                  "skills": [{"name": "Unity", "years_used": 4.0, "last_used_year": 2026}],
                  "experiences": [],
                  "education": [],
                  "shipped_titles": []
                }
                """;
            return Task.FromResult(Result.Success(json));
        }
    }

    private sealed class ResumeImportTestHarness : IAsyncDisposable
    {
        private readonly IMemoryCache _memoryCache;

        private ResumeImportTestHarness(
            AppDbContext dbContext,
            IMemoryCache memoryCache,
            AdjustableClock clock,
            Guid userId,
            Guid candidateId)
        {
            DbContext = dbContext;
            _memoryCache = memoryCache;
            Clock = clock;
            UserId = userId;
            CandidateId = candidateId;
        }

        public AppDbContext DbContext { get; }
        public AdjustableClock Clock { get; }
        public Guid UserId { get; }
        public Guid CandidateId { get; }

        public static async Task<ResumeImportTestHarness> CreateAsync()
        {
            var clock = new AdjustableClock();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"hgd-resume-import-tests-{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var dbContext = new AppDbContext(options, clock);

            dbContext.Cities.Add(new City
            {
                Id = 1,
                Name = "Bengaluru",
                Slug = "bengaluru",
                State = "Karnataka",
                StateCode = "KA",
                CountryCode = "IN",
                Aliases = ["Bangalore"],
                IsHub = true,
                SortOrder = 1,
                IsActive = true
            });

            dbContext.Skills.AddRange(
                new Skill
                {
                    Id = 1,
                    Name = "Unity",
                    Slug = "unity",
                    Category = SkillCategory.Engine,
                    Aliases = ["Unity3D"],
                    IsActive = true
                },
                new Skill
                {
                    Id = 2,
                    Name = "C#",
                    Slug = "csharp",
                    Category = SkillCategory.Language,
                    Aliases = ["CSharp"],
                    IsActive = true
                },
                new Skill
                {
                    Id = 3,
                    Name = "C++",
                    Slug = "cpp",
                    Category = SkillCategory.Language,
                    Aliases = ["CPP"],
                    IsActive = true
                },
                new Skill
                {
                    Id = 4,
                    Name = "Blender",
                    Slug = "blender",
                    Category = SkillCategory.ArtTool,
                    Aliases = [],
                    IsActive = true
                });

            dbContext.AppSettings.Add(new AppSetting
            {
                Key = CandidateResumeImportService.DailyParseLimitSettingKey,
                Value = "5",
                Description = "Daily resume parse limit",
                UpdatedAt = clock.UtcNow
            });

            var userId = Guid.NewGuid();
            var candidateId = Guid.NewGuid();

            dbContext.CandidateProfiles.Add(new CandidateProfile
            {
                Id = candidateId,
                UserId = userId,
                FullName = "Ananya Iyer",
                Headline = "Junior Developer",
                Bio = "Passionate game programmer in Bengaluru.",
                PrimaryCategory = DisciplineCategory.Programming,
                CityId = 1,
                Seniority = SeniorityLevel.Fresher,
                YearsExperience = 0.5m,
                Availability = CandidateAvailability.ActivelyLooking,
                Completeness = 20,
                OnboardingStep = 2,
                IsDiscoverable = true,
                LastActiveAt = clock.UtcNow
            });

            await dbContext.SaveChangesAsync();

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            return new ResumeImportTestHarness(dbContext, memoryCache, clock, userId, candidateId);
        }

        public CandidateResumeImportService CreateService(IResumeLlmClient? customLlmClient = null)
        {
            var auditLogger = new AuditLogger(DbContext, new NullIpAccessor(), Clock);
            var referenceData = new ReferenceDataService(DbContext, _memoryCache, Clock);
            var settingsService = new SettingsService(DbContext, _memoryCache, auditLogger, Clock);
            var pdfExtractor = new PdfPigResumeTextExtractor();
            var llmClient = customLlmClient ?? new DevResumeLlmClient(referenceData, Clock);
            var validator = new ApplyCandidateResumeDraftCommandValidator();
            var completeness = new CandidateCompletenessCalculator(DbContext);
            var evidence = new EvidenceService(DbContext);

            return new CandidateResumeImportService(
                DbContext,
                pdfExtractor,
                llmClient,
                referenceData,
                settingsService,
                validator,
                completeness,
                evidence,
                auditLogger,
                Clock,
                NullLogger<CandidateResumeImportService>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            _memoryCache.Dispose();
            await DbContext.DisposeAsync();
        }
    }

    private sealed class AdjustableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 10, 30, 0, TimeSpan.Zero);
    }

    private sealed class NullIpAccessor : IClientIpAccessor
    {
        public IPAddress? GetClientIpAddress() => null;
    }
}
