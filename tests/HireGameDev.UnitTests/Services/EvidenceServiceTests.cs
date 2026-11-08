using System.Net;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class EvidenceServiceTests
{
    private const int UnitySkillId = 1;
    private const int UnrealSkillId = 2;
    private const int CSkillId = 3;
    private const int CSharpSkillId = 4;
    private const int CppSkillId = 5;
    private const int GoSkillId = 6;
    private const int HlslSkillId = 7;

    [Fact]
    public async Task Step3Save_SkillsWithoutSupportingEvidence_AreClassifiedAsClaimed()
    {
        await using var harness = await TestHarness.CreateAsync();

        var saveResult = await harness.SkillsService.SaveSkillsAsync(new SaveCandidateSkillsCommand(
            UserId: harness.UserId,
            Skills:
            [
                new CandidateSkillSelectionInput(UnitySkillId, SkillProficiency.Strong, 3.0m, 2026, IsPrimary: true),
                new CandidateSkillSelectionInput(CSharpSkillId, SkillProficiency.Strong, 3.0m, 2026, IsPrimary: true),
                new CandidateSkillSelectionInput(HlslSkillId, SkillProficiency.Working, 1.5m, 2025, IsPrimary: false)
            ]));

        Assert.True(saveResult.IsSuccess);
        Assert.All(saveResult.Value, item =>
        {
            Assert.Equal(SkillEvidenceLevel.Claimed, item.EvidenceLevel);
            Assert.Equal("Self-declared", item.EvidenceLevel.ToDisplayLabel());
        });

        var persisted = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Claimed, persisted[UnitySkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, persisted[CSharpSkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, persisted[HlslSkillId]);
    }

    [Fact]
    public async Task Step2Save_ExperienceDescriptions_BackMatchedSkillsWithoutFalsePositives()
    {
        await using var harness = await TestHarness.CreateAsync();

        // Select C, C#, C++, Go, and HLSL on Step 3 first (all start Claimed).
        await harness.SaveSkillsAsync(CSkillId, CSharpSkillId, CppSkillId, GoSkillId, HlslSkillId);

        // Save Step 2 with tricky text:
        // - Mentions "C#", "C++20" (alias of C++), "Godot", "go-to", "Objective-C", and "CSS"
        //   -> MUST back C# and C++, and MUST NOT back C or Go!
        var step2First = await harness.BackgroundService.SaveStep2BackgroundAsync(new SaveCandidateStep2BackgroundCommand(
            UserId: harness.UserId,
            YearsExperience: 3.5m,
            Seniority: SeniorityLevel.Mid,
            Experiences:
            [
                new CandidateExperienceInput(
                    Id: null,
                    CompanyName: "Indie Forge",
                    Title: "Gameplay Programmer",
                    StartDate: new DateOnly(2023, 1, 1),
                    EndDate: null,
                    IsCurrent: true,
                    Description: "Built gameplay systems in C# and C++20; maintained Objective-C wrapper, CSS UI, and served as go-to Godot prototyper.")
            ],
            Education: []));

        Assert.True(step2First.IsSuccess);

        var levelsAfterFirst = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Backed, levelsAfterFirst[CSharpSkillId]);
        Assert.Equal(SkillEvidenceLevel.Backed, levelsAfterFirst[CppSkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, levelsAfterFirst[CSkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, levelsAfterFirst[GoSkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, levelsAfterFirst[HlslSkillId]);

        // Now add a second experience explicitly mentioning standalone C and Go.
        var existingExpId = step2First.Value.Experiences[0].Id;
        var step2Second = await harness.BackgroundService.SaveStep2BackgroundAsync(new SaveCandidateStep2BackgroundCommand(
            UserId: harness.UserId,
            YearsExperience: 3.5m,
            Seniority: SeniorityLevel.Mid,
            Experiences:
            [
                new CandidateExperienceInput(
                    Id: existingExpId,
                    CompanyName: "Indie Forge",
                    Title: "Gameplay Programmer",
                    StartDate: new DateOnly(2023, 1, 1),
                    EndDate: null,
                    IsCurrent: true,
                    Description: "Built gameplay systems in C# and C++20."),
                new CandidateExperienceInput(
                    Id: null,
                    CompanyName: "Netcode Labs",
                    Title: "Backend & Systems Engineer",
                    StartDate: new DateOnly(2021, 6, 1),
                    EndDate: new DateOnly(2022, 12, 1),
                    IsCurrent: false,
                    Description: "Wrote native packet codecs in C and matchmaking services in Go.")
            ],
            Education: []));

        Assert.True(step2Second.IsSuccess);

        var levelsAfterSecond = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Backed, levelsAfterSecond[CSkillId]);
        Assert.Equal(SkillEvidenceLevel.Backed, levelsAfterSecond[GoSkillId]);
        Assert.Equal("Supporting evidence submitted", levelsAfterSecond[GoSkillId].ToDisplayLabel());
    }

    [Fact]
    public async Task Step4Save_PortfolioSkillTagging_UpgradesToBackedNeverVerified_AndDowngradesWhenUntagged()
    {
        await using var harness = await TestHarness.CreateAsync();
        await harness.SaveSkillsAsync(UnitySkillId, CSharpSkillId, HlslSkillId);

        var saveLinkResult = await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles: [],
            PortfolioLinks:
            [
                new SavePortfolioLinkInput(
                    Id: null,
                    LinkType: PortfolioLinkType.Github,
                    Url: "https://github.com/dev/custom-urp-shaders",
                    Title: "Custom URP HLSL Shader Library",
                    SkillIds: [UnitySkillId, HlslSkillId])
            ]));

        Assert.True(saveLinkResult.IsSuccess);

        var afterTag = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Backed, afterTag[UnitySkillId]);
        Assert.Equal(SkillEvidenceLevel.Backed, afterTag[HlslSkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, afterTag[CSharpSkillId]);

        // Untag HLSL while keeping Unity on the existing portfolio link -> HLSL downgrades to Claimed.
        var linkId = saveLinkResult.Value.PortfolioLinks[0].Id;
        var updateLinkResult = await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles: [],
            PortfolioLinks:
            [
                new SavePortfolioLinkInput(
                    Id: linkId,
                    LinkType: PortfolioLinkType.Github,
                    Url: "https://github.com/dev/custom-urp-shaders",
                    Title: "Custom URP Shader Library",
                    SkillIds: [UnitySkillId])
            ]));

        Assert.True(updateLinkResult.IsSuccess);

        var afterUntag = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Backed, afterUntag[UnitySkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, afterUntag[HlslSkillId]);
    }

    [Fact]
    public async Task Step4Save_NdaShippedTitles_ContributeToInternalEvidenceWhileRedactingPublicProjection()
    {
        await using var harness = await TestHarness.CreateAsync();
        await harness.SaveSkillsAsync(UnitySkillId, UnrealSkillId, CppSkillId);

        var step4Result = await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles:
            [
                // NDA title #1: project hidden publicly, studio visible publicly
                new SaveShippedTitleInput(
                    Id: null,
                    GameName: "Project Titan",
                    StudioName: "Mumbai AAA Studio",
                    Role: "Engine Programmer",
                    Platforms: ["pc", "console"],
                    EngineSkillId: UnrealSkillId,
                    ReleaseYear: 2026,
                    StoreUrl: "https://store.steampowered.com/app/100001",
                    IsNda: true,
                    HideStudioInPublic: false),
                // NDA title #2: BOTH project and studio hidden publicly
                new SaveShippedTitleInput(
                    Id: null,
                    GameName: "Project Nebula",
                    StudioName: "Secret Co-Dev Partner",
                    Role: "Gameplay Programmer",
                    Platforms: ["android", "ios"],
                    EngineSkillId: UnitySkillId,
                    ReleaseYear: 2025,
                    StoreUrl: null,
                    IsNda: true,
                    HideStudioInPublic: true)
            ],
            PortfolioLinks: []));

        Assert.True(step4Result.IsSuccess);

        // Both NDA titles contribute to internal skill evidence!
        var levels = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Backed, levels[UnrealSkillId]);
        Assert.Equal(SkillEvidenceLevel.Backed, levels[UnitySkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, levels[CppSkillId]);

        // Verify public proof still redacts NDA details properly.
        var publicProof = await harness.ProofService.GetPublicProofForCandidateAsync(harness.CandidateId);
        Assert.Equal(2, publicProof.ShippedTitles.Count);
        Assert.Equal(PublicShippedTitleItem.NdaProjectLabel, publicProof.ShippedTitles[0].DisplayTitle);
        Assert.Equal("Mumbai AAA Studio", publicProof.ShippedTitles[0].DisplayStudio);
        Assert.Null(publicProof.ShippedTitles[0].PublicStoreUrl);

        Assert.Equal(PublicShippedTitleItem.NdaProjectLabel, publicProof.ShippedTitles[1].DisplayTitle);
        Assert.Equal(PublicShippedTitleItem.NdaStudioLabel, publicProof.ShippedTitles[1].DisplayStudio);
    }

    [Fact]
    public async Task ShippedTitleVerification_TitleVerifiedGivesBacked_CreditVerifiedGivesVerified_AndIdentityEditResetsToBacked()
    {
        await using var harness = await TestHarness.CreateAsync();
        await harness.SaveSkillsAsync(UnitySkillId, UnrealSkillId, CSharpSkillId);

        var step4Result = await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles:
            [
                new SaveShippedTitleInput(
                    Id: null,
                    GameName: "Raji Legacy",
                    StudioName: "Pune Studio",
                    Role: "Lead Programmer",
                    Platforms: ["pc"],
                    EngineSkillId: UnrealSkillId,
                    ReleaseYear: 2024,
                    StoreUrl: "https://store.steampowered.com/app/200001",
                    IsNda: false,
                    HideStudioInPublic: false),
                new SaveShippedTitleInput(
                    Id: null,
                    GameName: "Sky Cricket",
                    StudioName: "Bengaluru Mobile Games",
                    Role: "Client Engineer",
                    Platforms: ["android"],
                    EngineSkillId: UnitySkillId,
                    ReleaseYear: 2025,
                    StoreUrl: "https://play.google.com/store/apps/details?id=com.example.sky",
                    IsNda: false,
                    HideStudioInPublic: false)
            ],
            PortfolioLinks: []));

        Assert.True(step4Result.IsSuccess);
        var unrealTitleId = step4Result.Value.ShippedTitles[0].Id;
        var unityTitleId = step4Result.Value.ShippedTitles[1].Id;

        // Simulate Phase 5 backend verification:
        // - Unity title -> TitleVerified (store listing verified, individual credit unverified => Backed = 1)
        // - Unreal title -> CreditVerified (individual game credit verified => Verified = 2)
        var unityTitleEntity = await harness.DbContext.ShippedTitles.SingleAsync(t => t.Id == unityTitleId);
        var unrealTitleEntity = await harness.DbContext.ShippedTitles.SingleAsync(t => t.Id == unrealTitleId);
        unityTitleEntity.Verification = ShippedTitleVerification.TitleVerified;
        unityTitleEntity.VerifiedAt = harness.Clock.UtcNow;
        unrealTitleEntity.Verification = ShippedTitleVerification.CreditVerified;
        unrealTitleEntity.VerifiedAt = harness.Clock.UtcNow;
        await harness.DbContext.SaveChangesAsync();

        await harness.EvidenceService.RecomputeAndSaveAsync(harness.CandidateId);

        var levelsAfterBackendVerify = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Backed, levelsAfterBackendVerify[UnitySkillId]);
        Assert.Equal(SkillEvidenceLevel.Verified, levelsAfterBackendVerify[UnrealSkillId]);
        Assert.Equal("Independently verified", levelsAfterBackendVerify[UnrealSkillId].ToDisplayLabel());

        // Candidate edits GameName on the CreditVerified Unreal title in Step 4:
        // CandidateProofService resets Verification to None, and EvidenceService downgrades Unreal from Verified -> Backed
        // within the exact same Step 4 save operation!
        var editedStep4 = await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles:
            [
                new SaveShippedTitleInput(
                    Id: unrealTitleId,
                    GameName: "Different Unverified Game",
                    StudioName: "Pune Studio",
                    Role: "Lead Programmer",
                    Platforms: ["pc"],
                    EngineSkillId: UnrealSkillId,
                    ReleaseYear: 2024,
                    StoreUrl: "https://store.steampowered.com/app/200001",
                    IsNda: false,
                    HideStudioInPublic: false)
            ],
            PortfolioLinks: []));

        Assert.True(editedStep4.IsSuccess);

        var levelsAfterIdentityEdit = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Backed, levelsAfterIdentityEdit[UnrealSkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, levelsAfterIdentityEdit[UnitySkillId]); // Unity title was deleted!
    }

    [Fact]
    public async Task MultipleEvidenceSources_RetainsBackedUntilLastSourceRemoved_AndIsIdempotent()
    {
        await using var harness = await TestHarness.CreateAsync();
        await harness.SaveSkillsAsync(UnitySkillId, CSharpSkillId, HlslSkillId);

        // Source 1: Step 2 work experience mentions Unity.
        var expSave = await harness.BackgroundService.SaveStep2BackgroundAsync(new SaveCandidateStep2BackgroundCommand(
            UserId: harness.UserId,
            YearsExperience: 2.0m,
            Seniority: SeniorityLevel.Junior,
            Experiences:
            [
                new CandidateExperienceInput(
                    Id: null,
                    CompanyName: "Arcade Studio",
                    Title: "Unity Developer",
                    StartDate: new DateOnly(2024, 1, 1),
                    EndDate: null,
                    IsCurrent: true,
                    Description: "Built UI and camera systems in Unity.")
            ],
            Education: []));
        Assert.True(expSave.IsSuccess);

        // Source 2 & Source 3: Step 4 adds BOTH a Unity shipped title AND a portfolio link tagging Unity.
        var proofSave = await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles:
            [
                new SaveShippedTitleInput(
                    Id: null,
                    GameName: "Metro Runner",
                    StudioName: "Arcade Studio",
                    Role: "Gameplay Dev",
                    Platforms: ["android"],
                    EngineSkillId: UnitySkillId,
                    ReleaseYear: 2025,
                    StoreUrl: null,
                    IsNda: false,
                    HideStudioInPublic: false)
            ],
            PortfolioLinks:
            [
                new SavePortfolioLinkInput(
                    Id: null,
                    LinkType: PortfolioLinkType.Itchio,
                    Url: "https://arcadedev.itch.io/metro-runner",
                    Title: "Playable Build",
                    SkillIds: [UnitySkillId])
            ]));
        Assert.True(proofSave.IsSuccess);

        Assert.Equal(SkillEvidenceLevel.Backed, (await harness.GetEvidenceBySkillIdAsync())[UnitySkillId]);

        // Idempotency check: re-running RecomputeAndApplyAsync leaves tracked CandidateSkills Unchanged.
        harness.DbContext.ChangeTracker.Clear();
        var recomputedAgain = await harness.EvidenceService.RecomputeAndApplyAsync(harness.CandidateId);
        Assert.Equal(SkillEvidenceLevel.Backed, recomputedAgain[UnitySkillId]);
        Assert.All(
            harness.DbContext.ChangeTracker.Entries<CandidateSkill>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));

        // Remove Source 1 (experience description cleared) -> still Backed by title + portfolio link!
        await harness.BackgroundService.SaveStep2BackgroundAsync(new SaveCandidateStep2BackgroundCommand(
            UserId: harness.UserId,
            YearsExperience: 2.0m,
            Seniority: SeniorityLevel.Junior,
            Experiences: [],
            Education: []));
        Assert.Equal(SkillEvidenceLevel.Backed, (await harness.GetEvidenceBySkillIdAsync())[UnitySkillId]);

        // Remove Source 2 (shipped title deleted, portfolio link kept) -> still Backed by portfolio link!
        var linkId = proofSave.Value.PortfolioLinks[0].Id;
        await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles: [],
            PortfolioLinks:
            [
                new SavePortfolioLinkInput(
                    Id: linkId,
                    LinkType: PortfolioLinkType.Itchio,
                    Url: "https://arcadedev.itch.io/metro-runner",
                    Title: "Playable Build",
                    SkillIds: [UnitySkillId])
            ]));
        Assert.Equal(SkillEvidenceLevel.Backed, (await harness.GetEvidenceBySkillIdAsync())[UnitySkillId]);

        // Remove Source 3 (portfolio link deleted) -> no evidence left, downgrades to Claimed!
        await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles: [],
            PortfolioLinks: []));
        Assert.Equal(SkillEvidenceLevel.Claimed, (await harness.GetEvidenceBySkillIdAsync())[UnitySkillId]);
    }

    [Fact]
    public async Task FutureVerifiedEvidenceProvider_SkillChallengeVerification_IsNeverDowngradedByStep2Step3OrStep4Saves()
    {
        // Simulate a Phase 7 Skill Challenge provider reporting C++ and Go as passed/verified for this candidate.
        var challengeProvider = new StubVerifiedSkillEvidenceProvider(CppSkillId, GoSkillId);
        await using var harness = await TestHarness.CreateAsync([challengeProvider]);

        // Step 3 save: C++ and Go immediately receive Verified (2), while Hlsl is Claimed (0).
        var step3Initial = await harness.SkillsService.SaveSkillsAsync(new SaveCandidateSkillsCommand(
            UserId: harness.UserId,
            Skills:
            [
                new CandidateSkillSelectionInput(CppSkillId, SkillProficiency.Expert, 5.0m, 2026, IsPrimary: true),
                new CandidateSkillSelectionInput(GoSkillId, SkillProficiency.Strong, 2.5m, 2026, IsPrimary: true),
                new CandidateSkillSelectionInput(HlslSkillId, SkillProficiency.Working, 1.0m, 2025, IsPrimary: false)
            ]));

        Assert.True(step3Initial.IsSuccess);
        Assert.Equal(SkillEvidenceLevel.Verified, step3Initial.Value.Single(s => s.SkillId == CppSkillId).EvidenceLevel);
        Assert.Equal(SkillEvidenceLevel.Verified, step3Initial.Value.Single(s => s.SkillId == GoSkillId).EvidenceLevel);
        Assert.Equal(SkillEvidenceLevel.Claimed, step3Initial.Value.Single(s => s.SkillId == HlslSkillId).EvidenceLevel);

        // Step 2 save with empty experiences -> C++ and Go MUST remain Verified (never downgraded).
        var step2Save = await harness.BackgroundService.SaveStep2BackgroundAsync(new SaveCandidateStep2BackgroundCommand(
            UserId: harness.UserId,
            YearsExperience: 5.0m,
            Seniority: SeniorityLevel.Mid,
            Experiences: [],
            Education: []));
        Assert.True(step2Save.IsSuccess);

        // Step 4 save deleting/omitting all shipped titles and portfolio links -> C++ and Go MUST remain Verified.
        var step4Save = await harness.ProofService.SaveStep4ProofAsync(new SaveCandidateStep4ProofCommand(
            UserId: harness.UserId,
            ShippedTitles: [],
            PortfolioLinks: []));
        Assert.True(step4Save.IsSuccess);

        // Another Step 3 save modifying proficiency/years -> C++ and Go MUST remain Verified.
        var step3Resave = await harness.SkillsService.SaveSkillsAsync(new SaveCandidateSkillsCommand(
            UserId: harness.UserId,
            Skills:
            [
                new CandidateSkillSelectionInput(CppSkillId, SkillProficiency.Expert, 5.5m, 2026, IsPrimary: true),
                new CandidateSkillSelectionInput(GoSkillId, SkillProficiency.Expert, 3.0m, 2026, IsPrimary: true),
                new CandidateSkillSelectionInput(HlslSkillId, SkillProficiency.Strong, 1.5m, 2026, IsPrimary: false)
            ]));
        Assert.True(step3Resave.IsSuccess);

        var persisted = await harness.GetEvidenceBySkillIdAsync();
        Assert.Equal(SkillEvidenceLevel.Verified, persisted[CppSkillId]);
        Assert.Equal(SkillEvidenceLevel.Verified, persisted[GoSkillId]);
        Assert.Equal(SkillEvidenceLevel.Claimed, persisted[HlslSkillId]);
    }

    private sealed class StubVerifiedSkillEvidenceProvider : IVerifiedSkillEvidenceProvider
    {
        private readonly HashSet<int> _verifiedSkillIds;

        public StubVerifiedSkillEvidenceProvider(params int[] verifiedSkillIds)
        {
            _verifiedSkillIds = verifiedSkillIds.ToHashSet();
        }

        public Task<IReadOnlySet<int>> GetVerifiedSkillIdsAsync(
            Guid candidateId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlySet<int>>(_verifiedSkillIds);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class NullClientIpAccessor : IClientIpAccessor
    {
        public IPAddress? GetClientIpAddress() => IPAddress.Loopback;
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private TestHarness(
            AppDbContext dbContext,
            IClock clock,
            EvidenceService evidenceService,
            CandidateSkillsService skillsService,
            CandidateBackgroundService backgroundService,
            CandidateProofService proofService,
            Guid userId,
            Guid candidateId)
        {
            DbContext = dbContext;
            Clock = clock;
            EvidenceService = evidenceService;
            SkillsService = skillsService;
            BackgroundService = backgroundService;
            ProofService = proofService;
            UserId = userId;
            CandidateId = candidateId;
        }

        public AppDbContext DbContext { get; }
        public IClock Clock { get; }
        public EvidenceService EvidenceService { get; }
        public CandidateSkillsService SkillsService { get; }
        public CandidateBackgroundService BackgroundService { get; }
        public CandidateProofService ProofService { get; }
        public Guid UserId { get; }
        public Guid CandidateId { get; }

        public static async Task<TestHarness> CreateAsync(
            IEnumerable<IVerifiedSkillEvidenceProvider>? verifiedProviders = null)
        {
            var clock = new FixedClock();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"hgd-evidence-tests-{Guid.NewGuid():N}")
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
                IsHub = true,
                SortOrder = 1,
                IsActive = true
            });

            dbContext.Skills.AddRange(
                new Skill { Id = UnitySkillId, Name = "Unity", Slug = "unity", Category = SkillCategory.Engine, Aliases = ["Unity3D", "Unity 6"], IsActive = true, CreatedAt = clock.UtcNow },
                new Skill { Id = UnrealSkillId, Name = "Unreal Engine", Slug = "unreal-engine", Category = SkillCategory.Engine, Aliases = ["UE", "UE5", "Unreal Engine 5"], IsActive = true, CreatedAt = clock.UtcNow },
                new Skill { Id = CSkillId, Name = "C", Slug = "c", Category = SkillCategory.Language, Aliases = [], IsActive = true, CreatedAt = clock.UtcNow },
                new Skill { Id = CSharpSkillId, Name = "C#", Slug = "csharp", Category = SkillCategory.Language, Aliases = ["CSharp", ".NET", "DotNet"], IsActive = true, CreatedAt = clock.UtcNow },
                new Skill { Id = CppSkillId, Name = "C++", Slug = "cpp", Category = SkillCategory.Language, Aliases = ["CPP", "Modern C++", "C++17", "C++20"], IsActive = true, CreatedAt = clock.UtcNow },
                new Skill { Id = GoSkillId, Name = "Go", Slug = "go", Category = SkillCategory.Language, Aliases = ["Golang"], IsActive = true, CreatedAt = clock.UtcNow },
                new Skill { Id = HlslSkillId, Name = "HLSL", Slug = "hlsl", Category = SkillCategory.Language, Aliases = ["High-Level Shading Language"], IsActive = true, CreatedAt = clock.UtcNow });

            var userId = Guid.NewGuid();
            var candidateId = Guid.NewGuid();

            dbContext.CandidateProfiles.Add(new CandidateProfile
            {
                Id = candidateId,
                UserId = userId,
                FullName = "Aarav Rao",
                Headline = "Gameplay & Engine Programmer",
                PrimaryCategory = DisciplineCategory.Programming,
                CityId = 1,
                Seniority = SeniorityLevel.Mid,
                YearsExperience = 3.0m,
                Availability = CandidateAvailability.ActivelyLooking,
                OnboardingStep = 3,
                IsDiscoverable = true,
                LastActiveAt = clock.UtcNow
            });

            await dbContext.SaveChangesAsync();

            var auditLogger = new AuditLogger(dbContext, new NullClientIpAccessor(), clock);
            var completenessCalculator = new CandidateCompletenessCalculator(dbContext);
            var evidenceService = new EvidenceService(dbContext, verifiedProviders);

            var skillsService = new CandidateSkillsService(
                dbContext,
                new SaveCandidateSkillsCommandValidator(),
                completenessCalculator,
                evidenceService,
                auditLogger,
                clock);

            var backgroundService = new CandidateBackgroundService(
                dbContext,
                new SaveCandidateStep2BackgroundCommandValidator(),
                completenessCalculator,
                evidenceService,
                auditLogger,
                clock);

            var proofService = new CandidateProofService(
                dbContext,
                new SaveCandidateStep4ProofCommandValidator(),
                completenessCalculator,
                evidenceService,
                auditLogger,
                clock);

            return new TestHarness(
                dbContext,
                clock,
                evidenceService,
                skillsService,
                backgroundService,
                proofService,
                userId,
                candidateId);
        }

        public async Task SaveSkillsAsync(params int[] skillIds)
        {
            var inputs = skillIds
                .Select((id, idx) => new CandidateSkillSelectionInput(
                    SkillId: id,
                    Proficiency: SkillProficiency.Strong,
                    YearsUsed: 2.0m,
                    LastUsedYear: 2026,
                    IsPrimary: idx < 2))
                .ToArray();

            var result = await SkillsService.SaveSkillsAsync(new SaveCandidateSkillsCommand(UserId, inputs));
            Assert.True(result.IsSuccess, result.Error.Message);
        }

        public async Task<Dictionary<int, SkillEvidenceLevel>> GetEvidenceBySkillIdAsync()
        {
            return await DbContext.CandidateSkills
                .AsNoTracking()
                .Where(cs => cs.CandidateId == CandidateId)
                .ToDictionaryAsync(cs => cs.SkillId, cs => cs.EvidenceLevel);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
