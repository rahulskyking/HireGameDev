using System.Net;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class CandidateSettingsServiceTests
{
    [Fact]
    public async Task UpdatePrivacySettingsAsync_UpdatesDiscoverabilityAvailabilityAndCurrentCtcPrivacy()
    {
        await using var harness = await SettingsTestHarness.CreateAsync();

        var updateResult = await harness.Service.UpdatePrivacySettingsAsync(
            new UpdateCandidatePrivacySettingsCommand(
                UserId: harness.CandidateUserId,
                IsDiscoverable: false,
                Availability: CandidateAvailability.OpenToOffers,
                CurrentCtcLpa: 14.567m,
                ShowCurrentCtc: true));

        Assert.True(updateResult.IsSuccess, updateResult.Error.Message);
        Assert.False(updateResult.Value.IsDiscoverable);
        Assert.Equal(CandidateAvailability.OpenToOffers, updateResult.Value.Availability);
        Assert.Equal(14.57m, updateResult.Value.CurrentCtcLpa);
        Assert.True(updateResult.Value.ShowCurrentCtc);
        Assert.False(updateResult.Value.IsActivelyVisibleInStudioSearch);

        // Clearing CurrentCtcLpa automatically resets ShowCurrentCtc to false even if ShowCurrentCtc = true was sent.
        var clearedResult = await harness.Service.UpdatePrivacySettingsAsync(
            new UpdateCandidatePrivacySettingsCommand(
                UserId: harness.CandidateUserId,
                IsDiscoverable: true,
                Availability: CandidateAvailability.NotLooking,
                CurrentCtcLpa: null,
                ShowCurrentCtc: true));

        Assert.True(clearedResult.IsSuccess, clearedResult.Error.Message);
        Assert.True(clearedResult.Value.IsDiscoverable);
        Assert.Equal(80, clearedResult.Value.Completeness);
        Assert.True(clearedResult.Value.IsActivelyVisibleInStudioSearch);
        Assert.Equal(CandidateAvailability.NotLooking, clearedResult.Value.Availability);
        Assert.Null(clearedResult.Value.CurrentCtcLpa);
        Assert.False(clearedResult.Value.ShowCurrentCtc);

        var auditRows = await harness.DbContext.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == "candidate.settings.privacy_updated")
            .ToListAsync();
        Assert.Equal(2, auditRows.Count);
        Assert.DoesNotContain(auditRows, a => (a.AfterData ?? string.Empty).Contains("14.57", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchBlockAndUnblock_OnlyExposesVerifiedStudios_AndEnforcesDiscoveryBlocks()
    {
        await using var harness = await SettingsTestHarness.CreateAsync();

        var verifiedStudioId = Guid.NewGuid();
        var secondVerifiedStudioId = Guid.NewGuid();
        var pendingStudioId = Guid.NewGuid();
        var unverifiedStudioId = Guid.NewGuid();
        var deletedVerifiedStudioId = Guid.NewGuid();

        var employerRecruiterUserId = Guid.NewGuid();
        var otherRecruiterUserId = Guid.NewGuid();

        harness.DbContext.StudioProfiles.AddRange(
            new StudioProfile
            {
                Id = verifiedStudioId,
                OwnerUserId = employerRecruiterUserId,
                Name = "Rockstar India",
                Slug = "rockstar-india",
                CityId = 1,
                SizeBand = StudioSizeBand.Band200Plus,
                StudioType = StudioType.GameStudio,
                VerificationStatus = StudioVerificationStatus.Verified,
                VerifiedAt = harness.Clock.UtcNow
            },
            new StudioProfile
            {
                Id = secondVerifiedStudioId,
                OwnerUserId = otherRecruiterUserId,
                Name = "Rocket Pixel Interactive",
                Slug = "rocket-pixel-interactive",
                CityId = 1,
                SizeBand = StudioSizeBand.Band11To50,
                StudioType = StudioType.Indie,
                VerificationStatus = StudioVerificationStatus.Verified,
                VerifiedAt = harness.Clock.UtcNow
            },
            new StudioProfile
            {
                Id = pendingStudioId,
                OwnerUserId = Guid.NewGuid(),
                Name = "Rocksolid Pending Studio",
                Slug = "rocksolid-pending-studio",
                CityId = 1,
                SizeBand = StudioSizeBand.Band1To10,
                StudioType = StudioType.Indie,
                VerificationStatus = StudioVerificationStatus.Pending
            },
            new StudioProfile
            {
                Id = unverifiedStudioId,
                OwnerUserId = Guid.NewGuid(),
                Name = "Rock Unverified Draft",
                Slug = "rock-unverified-draft",
                CityId = 1,
                SizeBand = StudioSizeBand.Band1To10,
                StudioType = StudioType.Indie,
                VerificationStatus = StudioVerificationStatus.Unverified
            },
            new StudioProfile
            {
                Id = deletedVerifiedStudioId,
                OwnerUserId = Guid.NewGuid(),
                Name = "Rock Deleted Studio",
                Slug = "rock-deleted-studio",
                CityId = 1,
                SizeBand = StudioSizeBand.Band1To10,
                StudioType = StudioType.Indie,
                VerificationStatus = StudioVerificationStatus.Verified,
                DeletedAt = harness.Clock.UtcNow
            });

        harness.DbContext.StudioMembers.AddRange(
            new StudioMember
            {
                StudioId = verifiedStudioId,
                UserId = employerRecruiterUserId,
                MemberRole = StudioMemberRole.Recruiter,
                InvitedAt = harness.Clock.UtcNow,
                AcceptedAt = harness.Clock.UtcNow
            },
            new StudioMember
            {
                StudioId = secondVerifiedStudioId,
                UserId = otherRecruiterUserId,
                MemberRole = StudioMemberRole.Owner,
                InvitedAt = harness.Clock.UtcNow,
                AcceptedAt = harness.Clock.UtcNow
            });

        await harness.DbContext.SaveChangesAsync();

        // 1. Search strictly returns active Verified studios (never Pending, Unverified, or Soft-Deleted).
        var searchResult = await harness.Service.SearchStudiosToBlockAsync(harness.CandidateUserId, "rock");
        Assert.True(searchResult.IsSuccess, searchResult.Error.Message);
        Assert.Equal(
            new[] { "Rocket Pixel Interactive", "Rockstar India" },
            searchResult.Value.Select(s => s.Name).OrderBy(n => n).ToArray());
        Assert.All(searchResult.Value, s => Assert.False(s.IsAlreadyBlocked));

        // 2. Attempting to block an Unverified or Pending studio by GUID fails with NotFound.
        var blockPendingAttempt = await harness.Service.BlockStudioAsync(
            new BlockStudioCommand(harness.CandidateUserId, pendingStudioId));
        Assert.True(blockPendingAttempt.IsFailure);
        Assert.Equal(ErrorType.NotFound, blockPendingAttempt.Error.Type);

        // 3. Before blocking, both verified studio recruiters can discover the candidate (Completeness = 80, IsDiscoverable = true).
        Assert.True(await harness.Service.CanStudioViewerDiscoverCandidateAsync(harness.CandidateId, employerRecruiterUserId));
        Assert.True(await harness.Service.CanStudioViewerDiscoverCandidateAsync(harness.CandidateId, otherRecruiterUserId));

        // 4. Candidate blocks Rockstar India (and repeat call is idempotent).
        var blockResult = await harness.Service.BlockStudioAsync(
            new BlockStudioCommand(harness.CandidateUserId, verifiedStudioId));
        Assert.True(blockResult.IsSuccess, blockResult.Error.Message);
        Assert.Equal("rockstar-india", blockResult.Value.Slug);

        var idempotentBlock = await harness.Service.BlockStudioAsync(
            new BlockStudioCommand(harness.CandidateUserId, verifiedStudioId));
        Assert.True(idempotentBlock.IsSuccess);

        // Search now marks Rockstar India as IsAlreadyBlocked = true.
        var postBlockSearch = await harness.Service.SearchStudiosToBlockAsync(harness.CandidateUserId, "rockstar");
        var rockstarItem = Assert.Single(postBlockSearch.Value);
        Assert.True(rockstarItem.IsAlreadyBlocked);

        // Employer recruiter is now blocked, while other verified studio recruiter can still discover candidate.
        Assert.False(await harness.Service.CanStudioViewerDiscoverCandidateAsync(harness.CandidateId, employerRecruiterUserId));
        Assert.True(await harness.Service.CanStudioViewerDiscoverCandidateAsync(harness.CandidateId, otherRecruiterUserId));

        // Even if employerRecruiterUserId also joins secondVerifiedStudioId, they remain blocked because they belong to a blocked studio.
        harness.DbContext.StudioMembers.Add(new StudioMember
        {
            StudioId = secondVerifiedStudioId,
            UserId = employerRecruiterUserId,
            MemberRole = StudioMemberRole.Viewer,
            InvitedAt = harness.Clock.UtcNow,
            AcceptedAt = harness.Clock.UtcNow
        });
        await harness.DbContext.SaveChangesAsync();
        Assert.False(await harness.Service.CanStudioViewerDiscoverCandidateAsync(harness.CandidateId, employerRecruiterUserId));

        // 5. Unblocking restores discovery access (and repeat unblock is idempotent).
        var unblockResult = await harness.Service.UnblockStudioAsync(
            new UnblockStudioCommand(harness.CandidateUserId, verifiedStudioId));
        Assert.True(unblockResult.IsSuccess, unblockResult.Error.Message);

        var idempotentUnblock = await harness.Service.UnblockStudioAsync(
            new UnblockStudioCommand(harness.CandidateUserId, verifiedStudioId));
        Assert.True(idempotentUnblock.IsSuccess);

        Assert.True(await harness.Service.CanStudioViewerDiscoverCandidateAsync(harness.CandidateId, employerRecruiterUserId));
    }

    [Fact]
    public async Task BlockStudioAsync_EnforcesMaxBlockedStudiosCap()
    {
        await using var harness = await SettingsTestHarness.CreateAsync();

        for (var i = 0; i < CandidateProfileRules.MaxBlockedStudios; i++)
        {
            var studioId = Guid.NewGuid();
            harness.DbContext.StudioProfiles.Add(new StudioProfile
            {
                Id = studioId,
                OwnerUserId = Guid.NewGuid(),
                Name = $"Verified Studio {i:D2}",
                Slug = $"verified-studio-{i:D2}",
                CityId = 1,
                SizeBand = StudioSizeBand.Band1To10,
                StudioType = StudioType.Indie,
                VerificationStatus = StudioVerificationStatus.Verified,
                VerifiedAt = harness.Clock.UtcNow
            });
            harness.DbContext.CandidateStudioBlocks.Add(new CandidateStudioBlock
            {
                CandidateId = harness.CandidateId,
                StudioId = studioId,
                CreatedAt = harness.Clock.UtcNow
            });
        }

        var extraStudioId = Guid.NewGuid();
        harness.DbContext.StudioProfiles.Add(new StudioProfile
        {
            Id = extraStudioId,
            OwnerUserId = Guid.NewGuid(),
            Name = "Overflow Verified Studio",
            Slug = "overflow-verified-studio",
            CityId = 1,
            SizeBand = StudioSizeBand.Band1To10,
            StudioType = StudioType.Indie,
            VerificationStatus = StudioVerificationStatus.Verified,
            VerifiedAt = harness.Clock.UtcNow
        });
        await harness.DbContext.SaveChangesAsync();

        var overCapResult = await harness.Service.BlockStudioAsync(
            new BlockStudioCommand(harness.CandidateUserId, extraStudioId));

        Assert.True(overCapResult.IsFailure);
        Assert.Equal("CandidateSettings.BlockLimitReached", overCapResult.Error.Code);
    }

    private sealed class SettingsTestHarness : IAsyncDisposable
    {
        private SettingsTestHarness(
            AppDbContext dbContext,
            CandidateSettingsService service,
            FixedTestClock clock,
            Guid candidateUserId,
            Guid candidateId)
        {
            DbContext = dbContext;
            Service = service;
            Clock = clock;
            CandidateUserId = candidateUserId;
            CandidateId = candidateId;
        }

        public AppDbContext DbContext { get; }
        public CandidateSettingsService Service { get; }
        public FixedTestClock Clock { get; }
        public Guid CandidateUserId { get; }
        public Guid CandidateId { get; }

        public static async Task<SettingsTestHarness> CreateAsync()
        {
            var clock = new FixedTestClock();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"hgd-settings-tests-{Guid.NewGuid():N}")
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

            var candidateUserId = Guid.NewGuid();
            var candidateId = Guid.NewGuid();

            dbContext.CandidateProfiles.Add(new CandidateProfile
            {
                Id = candidateId,
                UserId = candidateUserId,
                FullName = "Arjun Rao",
                Headline = "Senior Gameplay Programmer · Unreal Engine 5",
                Bio = "Shipped multiplayer combat systems on PC and console.",
                PrimaryCategory = DisciplineCategory.Programming,
                CityId = 1,
                Seniority = SeniorityLevel.Senior,
                YearsExperience = 7.5m,
                ExpectedCtcLpa = 32.0m,
                CurrentCtcLpa = 24.0m,
                ShowCurrentCtc = false,
                NoticePeriodDays = 30,
                Availability = CandidateAvailability.ActivelyLooking,
                Completeness = 80,
                OnboardingStep = 5,
                IsDiscoverable = true,
                LastActiveAt = clock.UtcNow
            });

            for (var i = 1; i <= 5; i++)
            {
                dbContext.Skills.Add(new Skill
                {
                    Id = i,
                    Name = $"Skill {i}",
                    Slug = $"skill-{i}",
                    Category = SkillCategory.Language,
                    IsActive = true,
                    CreatedAt = clock.UtcNow
                });
                dbContext.CandidateSkills.Add(new CandidateSkill
                {
                    CandidateId = candidateId,
                    SkillId = i,
                    Proficiency = SkillProficiency.Strong,
                    IsPrimary = true
                });
            }

            dbContext.CandidateExperiences.Add(new CandidateExperience
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                CompanyName = "Indie Studio",
                Title = "Senior Gameplay Programmer",
                StartDate = new DateOnly(2020, 1, 1),
                IsCurrent = true
            });

            dbContext.ShippedTitles.Add(new ShippedTitle
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                GameName = "Starbound Arena",
                StudioName = "Indie Studio",
                Role = "Lead Combat Programmer",
                Platforms = new[] { "pc", "console" },
                ReleaseYear = 2024,
                SortOrder = 0
            });

            await dbContext.SaveChangesAsync();

            var completenessCalculator = new CandidateCompletenessCalculator(dbContext);
            var auditLogger = new AuditLogger(dbContext, new NullIpAccessor(), clock);

            var service = new CandidateSettingsService(
                dbContext,
                new UpdateCandidatePrivacySettingsCommandValidator(),
                new BlockStudioCommandValidator(),
                new UnblockStudioCommandValidator(),
                completenessCalculator,
                auditLogger,
                clock);

            return new SettingsTestHarness(
                dbContext,
                service,
                clock,
                candidateUserId,
                candidateId);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class FixedTestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class NullIpAccessor : IClientIpAccessor
    {
        public IPAddress? GetClientIpAddress() => null;
    }
}
