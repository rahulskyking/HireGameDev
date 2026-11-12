using System.Net;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Media;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class StudioServicesTests
{
    private const string ValidStudioDescription =
        "We are an independent PC and console game studio in Pune building original action-adventure IP powered by Unreal Engine 5 with a no-crunch, craft-driven engineering and art team.";

    [Fact]
    public async Task CreateStudioAsync_CreatesOwnerMembershipSkillsTitles_AndEnforcesSingleOwnedStudioAndEngineRequirement()
    {
        await using var harness = await StudioTestHarness.CreateAsync();

        // 1. Missing Engine skill fails validation even if Genre/Platform are passed.
        var missingEngineResult = await harness.ProfileService.CreateStudioAsync(new CreateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            Name: "Raji Makers",
            Slug: null,
            Website: "https://www.noddingheadsgames.com",
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band11To50,
            StudioType: StudioType.Indie,
            FoundedYear: 2017,
            CultureTags: ["indie_spirit", "original_ip", "no_crunch"],
            Gstin: "27ABCDE1234F1Z5",
            SkillIds: [harness.ActionRpgGenreId, harness.PcPlatformSkillId],
            Titles: []));

        Assert.True(missingEngineResult.IsFailure);
        Assert.Equal("StudioProfile.EngineRequired", missingEngineResult.Error.Code);

        // 2. Programming Language skill (C#) is rejected from studio_skills stack.
        var disallowedCategoryResult = await harness.ProfileService.CreateStudioAsync(new CreateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            Name: "Raji Makers",
            Slug: null,
            Website: null,
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band11To50,
            StudioType: StudioType.Indie,
            FoundedYear: 2017,
            CultureTags: [],
            Gstin: null,
            SkillIds: [harness.UnrealSkillId, harness.CSharpLanguageSkillId],
            Titles: []));

        Assert.True(disallowedCategoryResult.IsFailure);
        Assert.Equal("StudioProfile.InvalidSkillCategory", disallowedCategoryResult.Error.Code);

        // 3. Valid creation succeeds, creates owner row in studio_members, derives domain, and saves titles & skills.
        var createResult = await harness.ProfileService.CreateStudioAsync(new CreateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            Name: "Nodding Heads Games",
            Slug: null,
            Website: "https://www.noddingheadsgames.com/careers",
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band11To50,
            StudioType: StudioType.Indie,
            FoundedYear: 2017,
            CultureTags: ["indie_spirit", "original_ip", "no_crunch"],
            Gstin: "27abcde1234f1z5",
            SkillIds: [harness.UnrealSkillId, harness.ActionRpgGenreId, harness.PcPlatformSkillId],
            Titles:
            [
                new StudioTitleInput(
                    Id: null,
                    GameName: "Raji: An Ancient Epic",
                    ReleaseYear: 2020,
                    Platforms: ["PC", "Console", "Android", "iOS"],
                    StoreUrl: "https://store.steampowered.com/app/730390/Raji_An_Ancient_Epic/")
            ]));

        Assert.True(createResult.IsSuccess, createResult.Error.Message);
        var created = createResult.Value;
        Assert.Equal("nodding-heads-games", created.Slug);
        Assert.Equal("noddingheadsgames.com", created.CompanyEmailDomain);
        Assert.Equal("27ABCDE1234F1Z5", created.Gstin);
        Assert.Equal(StudioMemberRole.Owner, created.CallerRole);
        Assert.True(created.CanEditProfile);
        Assert.True(created.CanEditSlug);
        Assert.True(created.CanManageTeam);
        Assert.Equal(1, created.MemberCount);
        Assert.Equal(3, created.Skills.Count);
        Assert.Single(created.Titles);
        Assert.Equal(new[] { "pc", "console", "android", "ios" }, created.Titles[0].Platforms);

        var ownerAccount = await harness.DbContext.Users.SingleAsync(u => u.Id == harness.OwnerUserId);
        ownerAccount.AccountType = AccountType.Candidate;
        await harness.DbContext.SaveChangesAsync();
        var crossAccountCreation = await harness.ProfileService.CreateStudioAsync(new CreateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            Name: "Forbidden Studio",
            Slug: "forbidden-studio",
            Website: null,
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band1To10,
            StudioType: StudioType.Indie,
            FoundedYear: 2024,
            CultureTags: [],
            Gstin: null,
            SkillIds: [harness.UnitySkillId],
            Titles: []));
        Assert.True(crossAccountCreation.IsFailure);
        Assert.Equal("StudioProfile.SeparateAccountRequired", crossAccountCreation.Error.Code);
        ownerAccount.AccountType = AccountType.Studio;
        await harness.DbContext.SaveChangesAsync();

        // 4. Attempting to create a second active owned studio for the same user fails (ADR-018 Decision #3).
        var duplicateOwnedResult = await harness.ProfileService.CreateStudioAsync(new CreateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            Name: "Second Studio Attempt",
            Slug: "second-studio-attempt",
            Website: null,
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band1To10,
            StudioType: StudioType.Indie,
            FoundedYear: 2024,
            CultureTags: [],
            Gstin: null,
            SkillIds: [harness.UnitySkillId],
            Titles: []));

        Assert.True(duplicateOwnedResult.IsFailure);
        Assert.Equal(ErrorType.Conflict, duplicateOwnedResult.Error.Type);
        Assert.Equal("StudioProfile.AlreadyOwned", duplicateOwnedResult.Error.Code);
    }

    [Fact]
    public async Task SlugLockTeamRbacLogoUploadAndPublicVisibility_EnforceAllAdr018Rules()
    {
        await using var harness = await StudioTestHarness.CreateAsync();

        var createdResult = await harness.ProfileService.CreateStudioAsync(new CreateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            Name: "SuperGaming India",
            Slug: "supergaming",
            Website: "https://supergaming.com",
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band51To200,
            StudioType: StudioType.GameStudio,
            FoundedYear: 2019,
            CultureTags: ["original_ip", "health_insurance"],
            Gstin: null,
            SkillIds: [harness.UnrealSkillId, harness.UnitySkillId],
            Titles: []));
        Assert.True(createdResult.IsSuccess, createdResult.Error.Message);
        var studioId = createdResult.Value.Id;

        // A legacy Candidate account cannot gain Studio access through a team invitation.
        var recruiterAccount = await harness.DbContext.Users.SingleAsync(u => u.Id == harness.RecruiterUserId);
        recruiterAccount.AccountType = AccountType.Candidate;
        await harness.DbContext.SaveChangesAsync();
        var rejectedCandidate = await harness.TeamService.AddMemberByEmailAsync(new AddStudioMemberCommand(
            ActorUserId: harness.OwnerUserId,
            StudioId: studioId,
            Email: "recruiter@supergaming.com",
            MemberRole: StudioMemberRole.Recruiter));
        Assert.True(rejectedCandidate.IsFailure);
        Assert.Equal("StudioTeam.UserNotEligible", rejectedCandidate.Error.Code);
        Assert.False(await harness.DbContext.StudioMembers.AnyAsync(m => m.UserId == harness.RecruiterUserId));
        Assert.False(await harness.DbContext.UserRoles.AnyAsync(r => r.UserId == harness.RecruiterUserId));
        recruiterAccount.AccountType = AccountType.Studio;
        await harness.DbContext.SaveChangesAsync();

        // 1. Team management: Owner adds confirmed-email Studio user as Recruiter and another as Viewer.
        var addRecruiter = await harness.TeamService.AddMemberByEmailAsync(new AddStudioMemberCommand(
            ActorUserId: harness.OwnerUserId,
            StudioId: studioId,
            Email: "recruiter@supergaming.com",
            MemberRole: StudioMemberRole.Recruiter));
        Assert.True(addRecruiter.IsSuccess, addRecruiter.Error.Message);
        Assert.Equal(2, addRecruiter.Value.Members.Count);

        // Verify the added recruiter also received the 'studio' ASP.NET Identity role in user_roles.
        var studioRole = await harness.DbContext.Roles.SingleAsync(r => r.Name == HireGameDevRoleNames.Studio);
        Assert.True(await harness.DbContext.UserRoles.AnyAsync(
            ur => ur.UserId == harness.RecruiterUserId && ur.RoleId == studioRole.Id));

        var addViewer = await harness.TeamService.AddMemberByEmailAsync(new AddStudioMemberCommand(
            ActorUserId: harness.OwnerUserId,
            StudioId: studioId,
            Email: "viewer@supergaming.com",
            MemberRole: StudioMemberRole.Viewer));
        Assert.True(addViewer.IsSuccess, addViewer.Error.Message);
        Assert.Equal(3, addViewer.Value.Members.Count);

        // Unconfirmed email user cannot be added.
        var unconfirmedAttempt = await harness.TeamService.AddMemberByEmailAsync(new AddStudioMemberCommand(
            ActorUserId: harness.OwnerUserId,
            StudioId: studioId,
            Email: "unconfirmed@supergaming.com",
            MemberRole: StudioMemberRole.Viewer));
        Assert.True(unconfirmedAttempt.IsFailure);
        Assert.Equal("StudioTeam.UserNotEligible", unconfirmedAttempt.Error.Code);

        // Recruiter cannot manage team members (Owner only).
        var recruiterAddAttempt = await harness.TeamService.RemoveMemberAsync(new RemoveStudioMemberCommand(
            ActorUserId: harness.RecruiterUserId,
            StudioId: studioId,
            TargetUserId: harness.ViewerUserId));
        Assert.True(recruiterAddAttempt.IsFailure);
        Assert.Equal(ErrorType.Forbidden, recruiterAddAttempt.Error.Type);

        // Owner cannot remove or demote themselves.
        var ownerSelfRemove = await harness.TeamService.RemoveMemberAsync(new RemoveStudioMemberCommand(
            ActorUserId: harness.OwnerUserId,
            StudioId: studioId,
            TargetUserId: harness.OwnerUserId));
        Assert.True(ownerSelfRemove.IsFailure);
        Assert.Equal("StudioTeam.CannotRemoveOwner", ownerSelfRemove.Error.Code);

        // 2. Viewer cannot edit profile or upload logo; Recruiter CAN edit profile & upload logo, CANNOT change slug.
        var viewerEditAttempt = await harness.ProfileService.UpdateStudioProfileAsync(new UpdateStudioProfileCommand(
            ActorUserId: harness.ViewerUserId,
            StudioId: studioId,
            Name: "SuperGaming Updated",
            Slug: "supergaming",
            Website: "https://supergaming.com",
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band51To200,
            StudioType: StudioType.GameStudio,
            FoundedYear: 2019,
            CultureTags: ["original_ip"],
            Gstin: null,
            SkillIds: [harness.UnrealSkillId],
            Titles: []));
        Assert.True(viewerEditAttempt.IsFailure);
        Assert.Equal(ErrorType.Forbidden, viewerEditAttempt.Error.Type);

        var recruiterSlugChangeAttempt = await harness.ProfileService.UpdateStudioProfileAsync(new UpdateStudioProfileCommand(
            ActorUserId: harness.RecruiterUserId,
            StudioId: studioId,
            Name: "SuperGaming India",
            Slug: "supergaming-hq",
            Website: "https://supergaming.com",
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band51To200,
            StudioType: StudioType.GameStudio,
            FoundedYear: 2019,
            CultureTags: ["original_ip"],
            Gstin: null,
            SkillIds: [harness.UnrealSkillId],
            Titles: []));
        Assert.True(recruiterSlugChangeAttempt.IsFailure);
        Assert.Equal(ErrorType.Forbidden, recruiterSlugChangeAttempt.Error.Type);

        // 3. StudioLogoService: Recruiter uploads 800x400 PNG -> stored as <=256px WebP in StorageVisibility.Public.
        await using (var firstLogo = await CreatePngStreamAsync(800, 400))
        {
            var uploadLogo = await harness.LogoService.UploadLogoAsync(new UploadStudioLogoCommand(
                ActorUserId: harness.RecruiterUserId,
                StudioId: studioId,
                Content: firstLogo,
                FileName: "logo.png",
                DeclaredContentType: "image/png"));

            Assert.True(uploadLogo.IsSuccess, uploadLogo.Error.Message);
            Assert.StartsWith("logos/", uploadLogo.Value.LogoObjectKey!, StringComparison.Ordinal);
            Assert.EndsWith(".webp", uploadLogo.Value.LogoObjectKey!, StringComparison.Ordinal);
            Assert.StartsWith("https://cdn.hiregamedev.test/logos/", uploadLogo.Value.LogoUrl!, StringComparison.Ordinal);
            Assert.Equal(1, harness.FakeStorage.PublicObjectCount);
            Assert.Equal(0, harness.FakeStorage.PrivateObjectCount);
        }

        // 4. Public /studios/{slug} visibility while Unverified (ADR-018 Decision #1):
        var anonUnverified = await harness.ProfileService.GetPublicStudioBySlugAsync(
            "supergaming",
            new PublicStudioViewer(IsAuthenticated: false, UserId: null, IsAdmin: false));
        Assert.True(anonUnverified.IsFailure);
        Assert.Equal(ErrorType.NotFound, anonUnverified.Error.Type);

        var outsiderUnverified = await harness.ProfileService.GetPublicStudioBySlugAsync(
            "supergaming",
            new PublicStudioViewer(IsAuthenticated: true, UserId: Guid.NewGuid(), IsAdmin: false));
        Assert.True(outsiderUnverified.IsFailure);
        Assert.Equal(ErrorType.NotFound, outsiderUnverified.Error.Type);

        var viewerMemberPreview = await harness.ProfileService.GetPublicStudioBySlugAsync(
            "supergaming",
            new PublicStudioViewer(IsAuthenticated: true, UserId: harness.ViewerUserId, IsAdmin: false));
        Assert.True(viewerMemberPreview.IsSuccess, viewerMemberPreview.Error.Message);
        Assert.True(viewerMemberPreview.Value.IsPreviewMode);
        Assert.False(viewerMemberPreview.Value.IsVerified);
        Assert.Equal(StudioMemberRole.Viewer, viewerMemberPreview.Value.ViewerMemberRole);

        var adminPreview = await harness.ProfileService.GetPublicStudioBySlugAsync(
            "supergaming",
            new PublicStudioViewer(IsAuthenticated: true, UserId: Guid.NewGuid(), IsAdmin: true));
        Assert.True(adminPreview.IsSuccess, adminPreview.Error.Message);
        Assert.True(adminPreview.Value.IsPreviewMode);

        // 5. Transition studio to Verified -> slug becomes locked (ADR-018 Decision #4) and anonymous public view opens!
        var studioEntity = await harness.DbContext.StudioProfiles.SingleAsync(s => s.Id == studioId);
        studioEntity.VerificationStatus = StudioVerificationStatus.Verified;
        studioEntity.VerifiedAt = harness.Clock.UtcNow;
        await harness.DbContext.SaveChangesAsync();

        var ownerLockedSlugAttempt = await harness.ProfileService.UpdateStudioProfileAsync(new UpdateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            StudioId: studioId,
            Name: "SuperGaming India",
            Slug: "supergaming-renamed",
            Website: "https://supergaming.com",
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band51To200,
            StudioType: StudioType.GameStudio,
            FoundedYear: 2019,
            CultureTags: ["original_ip"],
            Gstin: null,
            SkillIds: [harness.UnrealSkillId],
            Titles: []));
        Assert.True(ownerLockedSlugAttempt.IsFailure);
        Assert.Equal("StudioProfile.SlugLocked", ownerLockedSlugAttempt.Error.Code);

        // ADR-019 Decision #3: Name and GSTIN are also locked while Pending or Verified.
        var ownerLockedIdentityAttempt = await harness.ProfileService.UpdateStudioProfileAsync(new UpdateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            StudioId: studioId,
            Name: "SuperGaming Studios",
            Slug: null,
            Website: "https://supergaming.com",
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band200Plus,
            StudioType: StudioType.GameStudio,
            FoundedYear: 2019,
            CultureTags: ["original_ip", "no_crunch"],
            Gstin: null,
            SkillIds: [harness.UnrealSkillId],
            Titles: []));
        Assert.True(ownerLockedIdentityAttempt.IsFailure);
        Assert.Equal("StudioProfile.IdentityLocked", ownerLockedIdentityAttempt.Error.Code);

        // Updating non-identity fields (SizeBand, CultureTags, Skills, Website, Description) while keeping Name/Slug/GSTIN unchanged succeeds when Verified.
        var ownerValidVerifiedEdit = await harness.ProfileService.UpdateStudioProfileAsync(new UpdateStudioProfileCommand(
            ActorUserId: harness.OwnerUserId,
            StudioId: studioId,
            Name: "SuperGaming India",
            Slug: null, // keeps existing locked slug
            Website: "https://supergaming.com",
            Description: ValidStudioDescription,
            CityId: harness.PuneCityId,
            SizeBand: StudioSizeBand.Band200Plus,
            StudioType: StudioType.GameStudio,
            FoundedYear: 2019,
            CultureTags: ["original_ip", "no_crunch"],
            Gstin: null,
            SkillIds: [harness.UnrealSkillId],
            Titles: []));
        Assert.True(ownerValidVerifiedEdit.IsSuccess, ownerValidVerifiedEdit.Error.Message);
        Assert.Equal("supergaming", ownerValidVerifiedEdit.Value.Slug);
        Assert.False(ownerValidVerifiedEdit.Value.CanEditSlug);
        Assert.False(ownerValidVerifiedEdit.Value.CanEditIdentityFields);

        // Anonymous visitor now sees the published Verified studio page!
        var anonVerified = await harness.ProfileService.GetPublicStudioBySlugAsync(
            "supergaming",
            new PublicStudioViewer(IsAuthenticated: false, UserId: null, IsAdmin: false));
        Assert.True(anonVerified.IsSuccess, anonVerified.Error.Message);
        Assert.True(anonVerified.Value.IsVerified);
        Assert.False(anonVerified.Value.IsPreviewMode);
        Assert.Equal("SuperGaming India", anonVerified.Value.Name);
        Assert.Single(anonVerified.Value.Engines);
    }

    private static async Task<MemoryStream> CreatePngStreamAsync(int width, int height)
    {
        var ms = new MemoryStream();
        using var img = new Image<Rgba32>(width, height, new Rgba32(240, 210, 20));
        await img.SaveAsync(ms, new PngEncoder());
        ms.Position = 0;
        return ms;
    }

    private sealed class StudioTestHarness : IAsyncDisposable
    {
        private StudioTestHarness(
            AppDbContext dbContext,
            FixedClock clock,
            InMemoryPublicStorageFake fakeStorage,
            StudioProfileService profileService,
            StudioLogoService logoService,
            StudioTeamService teamService,
            Guid ownerUserId,
            Guid recruiterUserId,
            Guid viewerUserId,
            int puneCityId,
            int unrealSkillId,
            int unitySkillId,
            int actionRpgGenreId,
            int pcPlatformSkillId,
            int cSharpLanguageSkillId)
        {
            DbContext = dbContext;
            Clock = clock;
            FakeStorage = fakeStorage;
            ProfileService = profileService;
            LogoService = logoService;
            TeamService = teamService;
            OwnerUserId = ownerUserId;
            RecruiterUserId = recruiterUserId;
            ViewerUserId = viewerUserId;
            PuneCityId = puneCityId;
            UnrealSkillId = unrealSkillId;
            UnitySkillId = unitySkillId;
            ActionRpgGenreId = actionRpgGenreId;
            PcPlatformSkillId = pcPlatformSkillId;
            CSharpLanguageSkillId = cSharpLanguageSkillId;
        }

        public AppDbContext DbContext { get; }
        public FixedClock Clock { get; }
        public InMemoryPublicStorageFake FakeStorage { get; }
        public StudioProfileService ProfileService { get; }
        public StudioLogoService LogoService { get; }
        public StudioTeamService TeamService { get; }
        public Guid OwnerUserId { get; }
        public Guid RecruiterUserId { get; }
        public Guid ViewerUserId { get; }
        public int PuneCityId { get; }
        public int UnrealSkillId { get; }
        public int UnitySkillId { get; }
        public int ActionRpgGenreId { get; }
        public int PcPlatformSkillId { get; }
        public int CSharpLanguageSkillId { get; }

        public static async Task<StudioTestHarness> CreateAsync()
        {
            var clock = new FixedClock();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"hgd-studio-tests-{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var dbContext = new AppDbContext(options, clock);

            dbContext.Cities.Add(new City
            {
                Id = 10,
                Name = "Pune",
                Slug = "pune",
                State = "Maharashtra",
                StateCode = "MH",
                CountryCode = "IN",
                IsHub = true,
                SortOrder = 2,
                IsActive = true
            });

            dbContext.Skills.AddRange(
                new Skill
                {
                    Id = 101,
                    Name = "Unreal Engine",
                    Slug = "unreal-engine",
                    Category = SkillCategory.Engine,
                    IsActive = true
                },
                new Skill
                {
                    Id = 102,
                    Name = "Unity",
                    Slug = "unity",
                    Category = SkillCategory.Engine,
                    IsActive = true
                },
                new Skill
                {
                    Id = 201,
                    Name = "Action RPG",
                    Slug = "action-rpg",
                    Category = SkillCategory.Genre,
                    IsActive = true
                },
                new Skill
                {
                    Id = 301,
                    Name = "PC / Steam",
                    Slug = "pc-steam",
                    Category = SkillCategory.Platform,
                    IsActive = true
                },
                new Skill
                {
                    Id = 401,
                    Name = "C#",
                    Slug = "csharp",
                    Category = SkillCategory.Language,
                    IsActive = true
                });

            var ownerUserId = Guid.NewGuid();
            var recruiterUserId = Guid.NewGuid();
            var viewerUserId = Guid.NewGuid();
            var unconfirmedUserId = Guid.NewGuid();

            dbContext.Users.AddRange(
                new ApplicationUser
                {
                    Id = ownerUserId,
                    UserName = "founder@supergaming.com",
                    NormalizedUserName = "FOUNDER@SUPERGAMING.COM",
                    Email = "founder@supergaming.com",
                    NormalizedEmail = "FOUNDER@SUPERGAMING.COM",
                    EmailConfirmed = true,
                    DisplayName = "Roby John",
                    AccountType = AccountType.Studio,
                    IsActive = true
                },
                new ApplicationUser
                {
                    Id = recruiterUserId,
                    UserName = "recruiter@supergaming.com",
                    NormalizedUserName = "RECRUITER@SUPERGAMING.COM",
                    Email = "recruiter@supergaming.com",
                    NormalizedEmail = "RECRUITER@SUPERGAMING.COM",
                    EmailConfirmed = true,
                    DisplayName = "Ananya Joshi",
                    AccountType = AccountType.Studio,
                    IsActive = true
                },
                new ApplicationUser
                {
                    Id = viewerUserId,
                    UserName = "viewer@supergaming.com",
                    NormalizedUserName = "VIEWER@SUPERGAMING.COM",
                    Email = "viewer@supergaming.com",
                    NormalizedEmail = "VIEWER@SUPERGAMING.COM",
                    EmailConfirmed = true,
                    DisplayName = "Vikram Kulkarni",
                    AccountType = AccountType.Studio,
                    IsActive = true
                },
                new ApplicationUser
                {
                    Id = unconfirmedUserId,
                    UserName = "unconfirmed@supergaming.com",
                    NormalizedUserName = "UNCONFIRMED@SUPERGAMING.COM",
                    Email = "unconfirmed@supergaming.com",
                    NormalizedEmail = "UNCONFIRMED@SUPERGAMING.COM",
                    EmailConfirmed = false,
                    DisplayName = "Pending Signup",
                    AccountType = AccountType.Studio,
                    IsActive = true
                });

            await dbContext.SaveChangesAsync();

            var fakeStorage = new InMemoryPublicStorageFake();
            var auditLogger = new AuditLogger(dbContext, new NullIpAccessor(), clock);
            var imageProcessor = new ImageSharpImageProcessor();

            var profileService = new StudioProfileService(dbContext, fakeStorage, auditLogger, clock);
            var logoService = new StudioLogoService(
                dbContext,
                imageProcessor,
                fakeStorage,
                auditLogger,
                NullLogger<StudioLogoService>.Instance);
            var teamService = new StudioTeamService(dbContext, auditLogger, clock);

            return new StudioTestHarness(
                dbContext,
                clock,
                fakeStorage,
                profileService,
                logoService,
                teamService,
                ownerUserId,
                recruiterUserId,
                viewerUserId,
                puneCityId: 10,
                unrealSkillId: 101,
                unitySkillId: 102,
                actionRpgGenreId: 201,
                pcPlatformSkillId: 301,
                cSharpLanguageSkillId: 401);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class InMemoryPublicStorageFake : IFileStorageService
    {
        private readonly Dictionary<string, byte[]> _private = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _public = new(StringComparer.Ordinal);

        public string ProviderName => "InMemoryTestStorage";
        public int PrivateObjectCount => _private.Count;
        public int PublicObjectCount => _public.Count;

        public async Task PutAsync(FileStoragePutRequest request, CancellationToken cancellationToken = default)
        {
            if (!ObjectKeyRules.TryNormalize(request.ObjectKey, out var normalized, out var err))
            {
                throw new ArgumentException(err, nameof(request));
            }

            using var ms = new MemoryStream();
            await request.Content.CopyToAsync(ms, cancellationToken);
            var bucket = request.Visibility == StorageVisibility.Public ? _public : _private;
            bucket[normalized] = ms.ToArray();
        }

        public Task<StoredObjectReadResult?> OpenReadAsync(
            string objectKey,
            StorageVisibility visibility,
            CancellationToken cancellationToken = default)
        {
            if (!ObjectKeyRules.TryNormalize(objectKey, out var normalized, out _))
            {
                return Task.FromResult<StoredObjectReadResult?>(null);
            }

            var bucket = visibility == StorageVisibility.Public ? _public : _private;
            if (!bucket.TryGetValue(normalized, out var bytes))
            {
                return Task.FromResult<StoredObjectReadResult?>(null);
            }

            return Task.FromResult<StoredObjectReadResult?>(new StoredObjectReadResult(
                Content: new MemoryStream(bytes, writable: false),
                ContentType: "image/webp",
                ContentLength: bytes.Length,
                ETag: $"\"{bytes.Length:x}\"",
                LastModifiedUtc: DateTimeOffset.UtcNow));
        }

        public Task<bool> DeleteIfExistsAsync(
            string objectKey,
            StorageVisibility visibility,
            CancellationToken cancellationToken = default)
        {
            if (!ObjectKeyRules.TryNormalize(objectKey, out var normalized, out _))
            {
                return Task.FromResult(false);
            }

            var bucket = visibility == StorageVisibility.Public ? _public : _private;
            return Task.FromResult(bucket.Remove(normalized));
        }

        public string ResolvePublicUrl(string objectKey)
        {
            if (!ObjectKeyRules.TryNormalize(objectKey, out var normalized, out var err))
            {
                throw new ArgumentException(err, nameof(objectKey));
            }

            return $"https://cdn.hiregamedev.test/{normalized}";
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class NullIpAccessor : IClientIpAccessor
    {
        public IPAddress? GetClientIpAddress() => null;
    }
}
