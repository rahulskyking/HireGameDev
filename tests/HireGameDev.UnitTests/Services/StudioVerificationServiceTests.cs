using System.Net;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Email;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HireGameDev.UnitTests.Services;

public sealed class StudioVerificationServiceTests
{
    private const string ValidDescription =
        "Independent Indian game studio building atmospheric narrative action-adventure games for PC and console players.";

    [Fact]
    public async Task SubmitQueueReviewRejectResubmitApproveAndRevoke_EnforcesAllAdr010AndAdr019Rules()
    {
        await using var harness = await VerificationTestHarness.CreateAsync();

        // 1. Bootstrap admin seeder grants 'admin' role to configured verified bootstrap email and writes system audit row.
        var seeder = new AdminBootstrapSeeder(
            harness.DbContext,
            harness.AuditLogger,
            Options.Create(new AdminOptions
            {
                BootstrapEmails = ["admin@hiregamedev.com", "unconfirmed-admin@hiregamedev.com"]
            }),
            NullLogger<AdminBootstrapSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync(); // idempotent second call

        var adminRole = await harness.DbContext.Roles.SingleAsync(r => r.Name == HireGameDevRoleNames.Admin);
        Assert.Equal(1, await harness.DbContext.UserRoles.CountAsync(ur => ur.RoleId == adminRole.Id));
        Assert.True(await harness.DbContext.UserRoles.AnyAsync(
            ur => ur.UserId == harness.AdminUserId && ur.RoleId == adminRole.Id));
        Assert.Equal(1, await harness.DbContext.AuditLogs.CountAsync(
            a => a.Action == "system.admin_bootstrap.role_granted"));

        // 2. Indie founder with Gmail creates a studio WITHOUT website/GSTIN/store URL initially.
        var created = await harness.ProfileService.CreateStudioAsync(new CreateStudioProfileCommand(
            ActorUserId: harness.IndieOwnerUserId,
            Name: "Monsoon Pixel Collective",
            Slug: "monsoon-pixel",
            Website: null,
            Description: ValidDescription,
            CityId: harness.BengaluruCityId,
            SizeBand: StudioSizeBand.Band1To10,
            StudioType: StudioType.Indie,
            FoundedYear: 2024,
            CultureTags: ["indie_spirit", "remote_friendly"],
            Gstin: null,
            SkillIds: [harness.GodotSkillId],
            Titles: []));
        Assert.True(created.IsSuccess, created.Error.Message);
        var studioId = created.Value.Id;

        var addRecruiter = await harness.TeamService.AddMemberByEmailAsync(new AddStudioMemberCommand(
            ActorUserId: harness.IndieOwnerUserId,
            StudioId: studioId,
            Email: "recruiter@monsoonpixel.in",
            MemberRole: StudioMemberRole.Recruiter));
        Assert.True(addRecruiter.IsSuccess, addRecruiter.Error.Message);

        // 3. Recruiter can view workspace status, but CANNOT submit verification (ADR-019 Decision #2).
        var recruiterWorkspace = await harness.VerificationService.GetVerificationWorkspaceAsync(
            harness.RecruiterUserId,
            studioId);
        Assert.True(recruiterWorkspace.IsSuccess, recruiterWorkspace.Error.Message);
        Assert.False(recruiterWorkspace.Value.IsOwner);
        Assert.False(recruiterWorkspace.Value.CanSubmitNow);

        var recruiterSubmit = await harness.VerificationService.SubmitForVerificationAsync(
            new SubmitStudioVerificationCommand(
                ActorUserId: harness.RecruiterUserId,
                StudioId: studioId,
                Website: null,
                Gstin: null,
                ProofUrls: ["https://store.steampowered.com/app/123456/Monsoon_Tales/"],
                ApplicantNotes: "Trying to submit as recruiter."));
        Assert.True(recruiterSubmit.IsFailure);
        Assert.Equal(ErrorType.Forbidden, recruiterSubmit.Error.Type);
        Assert.Equal("StudioVerification.OwnerOnly", recruiterSubmit.Error.Code);

        // 4. Owner attempting to submit with zero public presence signals is blocked with actionable validation.
        var incompleteSubmit = await harness.VerificationService.SubmitForVerificationAsync(
            new SubmitStudioVerificationCommand(
                ActorUserId: harness.IndieOwnerUserId,
                StudioId: studioId,
                Website: null,
                Gstin: null,
                ProofUrls: [],
                ApplicantNotes: "No links yet."));
        Assert.True(incompleteSubmit.IsFailure);
        Assert.Equal("StudioVerification.PrerequisitesNotMet", incompleteSubmit.Error.Code);

        // 5. Indie Owner submits with Steam + itch.io proof links (no GSTIN / custom domain needed!).
        var firstSubmit = await harness.VerificationService.SubmitForVerificationAsync(
            new SubmitStudioVerificationCommand(
                ActorUserId: harness.IndieOwnerUserId,
                StudioId: studioId,
                Website: null,
                Gstin: null,
                ProofUrls:
                [
                    "https://store.steampowered.com/app/123456/Monsoon_Tales/",
                    "https://monsoonpixel.itch.io/monsoon-tales"
                ],
                ApplicantNotes: "2-person indie team in Bengaluru; Steam page lists our studio name."));
        Assert.True(firstSubmit.IsSuccess, firstSubmit.Error.Message);
        Assert.Equal(StudioVerificationStatus.Pending, firstSubmit.Value.VerificationStatus);
        Assert.Equal(StudioEmailDomainSignal.PersonalEmail, firstSubmit.Value.DomainAssessment.Signal);
        Assert.Equal(2, firstSubmit.Value.VerificationProofUrls.Count);
        Assert.Contains(harness.EmailSender.Sent, e => e.Tag == "studio_verification_submitted");

        // Duplicate submission while pending is rejected.
        var duplicateSubmit = await harness.VerificationService.SubmitForVerificationAsync(
            new SubmitStudioVerificationCommand(
                ActorUserId: harness.IndieOwnerUserId,
                StudioId: studioId,
                Website: null,
                Gstin: null,
                ProofUrls: ["https://monsoonpixel.itch.io/monsoon-tales"],
                ApplicantNotes: null));
        Assert.True(duplicateSubmit.IsFailure);
        Assert.Equal("StudioVerification.AlreadyPending", duplicateSubmit.Error.Code);

        // 6. Advance clock 26 hours -> Admin queue marks Pending item as past 24h target SLA.
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddHours(26);
        var pendingQueue = await harness.VerificationService.GetAdminQueueAsync(StudioVerificationStatus.Pending);
        Assert.Equal(1, pendingQueue.Counts.PendingCount);
        var queueItem = Assert.Single(pendingQueue.Items);
        Assert.Equal(studioId, queueItem.StudioId);
        Assert.True(queueItem.IsPastTargetSla);
        Assert.Equal(26.0, queueItem.WaitingHours);

        // 7. Non-admin cannot review; Admin without mandatory reason or rejection feedback is rejected (ADR-010).
        var nonAdminReview = await harness.VerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: harness.IndieOwnerUserId,
                StudioId: studioId,
                Decision: StudioVerificationDecision.Approve,
                Reason: "Trying to self-approve studio.",
                ReviewerFeedback: null));
        Assert.True(nonAdminReview.IsFailure);
        Assert.Equal(ErrorType.Forbidden, nonAdminReview.Error.Type);

        var missingReasonReview = await harness.VerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: harness.AdminUserId,
                StudioId: studioId,
                Decision: StudioVerificationDecision.Approve,
                Reason: "short",
                ReviewerFeedback: null));
        Assert.True(missingReasonReview.IsFailure);
        Assert.Equal("StudioVerification.ReasonRequired", missingReasonReview.Error.Code);

        var rejectWithoutFeedback = await harness.VerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: harness.AdminUserId,
                StudioId: studioId,
                Decision: StudioVerificationDecision.Reject,
                Reason: "Steam link returned 404 during verification check.",
                ReviewerFeedback: ""));
        Assert.True(rejectWithoutFeedback.IsFailure);
        Assert.Equal("StudioVerification.FeedbackRequired", rejectWithoutFeedback.Error.Code);

        // 8. Admin requests changes (Reject) -> studio transitions to Rejected, Owner can edit identity & resubmit!
        var rejected = await harness.VerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: harness.AdminUserId,
                StudioId: studioId,
                Decision: StudioVerificationDecision.Reject,
                Reason: "itch.io page did not list founder contact; asked for LinkedIn or updated itch bio.",
                ReviewerFeedback: "Please add your HireGameDev studio slug or founder name on your itch.io profile bio and resubmit."));
        Assert.True(rejected.IsSuccess, rejected.Error.Message);
        Assert.Equal(StudioVerificationStatus.Rejected, rejected.Value.NewStatus);
        Assert.Contains(harness.EmailSender.Sent, e => e.Tag == "studio_verification_changes_requested");

        var resubmitted = await harness.VerificationService.SubmitForVerificationAsync(
            new SubmitStudioVerificationCommand(
                ActorUserId: harness.IndieOwnerUserId,
                StudioId: studioId,
                Website: "https://monsoonpixel.in",
                Gstin: null,
                ProofUrls:
                [
                    "https://store.steampowered.com/app/123456/Monsoon_Tales/",
                    "https://www.linkedin.com/company/monsoon-pixel/"
                ],
                ApplicantNotes: "Added official website https://monsoonpixel.in and company LinkedIn page."));
        Assert.True(resubmitted.IsSuccess, resubmitted.Error.Message);
        Assert.Equal(StudioVerificationStatus.Pending, resubmitted.Value.VerificationStatus);
        // Root HTTPS URLs are canonicalized through Uri.AbsoluteUri, including the trailing slash.
        Assert.Equal("https://monsoonpixel.in/", resubmitted.Value.Website);

        // 9. Admin approves -> VerifiedAt & VerifiedBy populated, mandatory audit log persisted, public page live!
        harness.Clock.UtcNow = harness.Clock.UtcNow.AddHours(2);
        var approved = await harness.VerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: harness.AdminUserId,
                StudioId: studioId,
                Decision: StudioVerificationDecision.Approve,
                Reason: "Verified Steam publisher page + active domain monsoonpixel.in + LinkedIn company page.",
                ReviewerFeedback: "Welcome aboard! Your public studio page and job publishing are now unlocked."));
        Assert.True(approved.IsSuccess, approved.Error.Message);
        Assert.Equal(StudioVerificationStatus.Verified, approved.Value.NewStatus);
        Assert.Contains(harness.EmailSender.Sent, e => e.Tag == "studio_verification_approved");

        var verifiedEntity = await harness.DbContext.StudioProfiles.AsNoTracking().SingleAsync(s => s.Id == studioId);
        Assert.Equal(StudioVerificationStatus.Verified, verifiedEntity.VerificationStatus);
        Assert.Equal(harness.AdminUserId, verifiedEntity.VerifiedBy);
        Assert.Equal(harness.Clock.UtcNow, verifiedEntity.VerifiedAt);

        Assert.True(await harness.DbContext.AuditLogs.AnyAsync(
            a => a.Action == "admin.studio_verification.approve" &&
                 a.ActorUserId == harness.AdminUserId &&
                 a.Reason != null));

        // 10. Controlled Staged Identity Re-Verification (ADR-019 Decision #3):
        //     Recruiter cannot request identity change; Owner stages legal incorporation + new slug + GSTIN.
        var recruiterIdentityAttempt = await harness.VerificationService.RequestIdentityChangeAsync(
            new RequestStudioIdentityChangeCommand(
                ActorUserId: harness.RecruiterUserId,
                StudioId: studioId,
                ProposedName: "Monsoon Pixel Studios Pvt Ltd",
                ProposedSlug: "monsoon-pixel-studios",
                ProposedGstin: "29ABCDE1234F1Z5",
                ProofUrls: ["https://monsoonpixel.in/legal"],
                ApplicantNotes: "Trying as recruiter."));
        Assert.True(recruiterIdentityAttempt.IsFailure);
        Assert.Equal(ErrorType.Forbidden, recruiterIdentityAttempt.Error.Type);

        var stagedRequest = await harness.VerificationService.RequestIdentityChangeAsync(
            new RequestStudioIdentityChangeCommand(
                ActorUserId: harness.IndieOwnerUserId,
                StudioId: studioId,
                ProposedName: "Monsoon Pixel Studios Pvt Ltd",
                ProposedSlug: "monsoon-pixel-studios",
                ProposedGstin: "29ABCDE1234F1Z5",
                ProofUrls: ["https://monsoonpixel.in/incorporation"],
                ApplicantNotes: "Incorporated as Monsoon Pixel Studios Pvt Ltd in Karnataka with GSTIN."));
        Assert.True(stagedRequest.IsSuccess, stagedRequest.Error.Message);
        Assert.Equal(StudioVerificationStatus.Verified, stagedRequest.Value.VerificationStatus);
        Assert.Equal("Monsoon Pixel Collective", stagedRequest.Value.StudioName); // Live identity untouched!
        Assert.Equal("monsoon-pixel", stagedRequest.Value.StudioSlug);
        Assert.NotNull(stagedRequest.Value.PendingIdentityChange);
        Assert.Equal("monsoon-pixel-studios", stagedRequest.Value.PendingIdentityChange!.ProposedSlug);
        Assert.Contains(harness.EmailSender.Sent, e => e.Tag == "studio_identity_change_submitted");

        // Staged slug is reserved so other studios cannot claim it, while live /studios/monsoon-pixel stays online!
        var reservedSlugCheck = await harness.ProfileService.CheckSlugAvailabilityAsync("monsoon-pixel-studios");
        Assert.True(reservedSlugCheck.IsSuccess);
        Assert.False(reservedSlugCheck.Value.IsAvailable);

        var liveAnonDuringStaging = await harness.ProfileService.GetPublicStudioBySlugAsync(
            "monsoon-pixel",
            new PublicStudioViewer(IsAuthenticated: false, UserId: null, IsAdmin: false));
        Assert.True(liveAnonDuringStaging.IsSuccess);
        Assert.Equal("Monsoon Pixel Collective", liveAnonDuringStaging.Value.Name);

        // Owner can cancel a staged identity change (releasing the reserved slug), then submit a revised one.
        var cancelled = await harness.VerificationService.CancelIdentityChangeAsync(
            new CancelStudioIdentityChangeCommand(harness.IndieOwnerUserId, studioId));
        Assert.True(cancelled.IsSuccess, cancelled.Error.Message);
        Assert.Null(cancelled.Value.PendingIdentityChange);

        var releasedSlugCheck = await harness.ProfileService.CheckSlugAvailabilityAsync("monsoon-pixel-studios");
        Assert.True(releasedSlugCheck.Value.IsAvailable);

        var reStaged = await harness.VerificationService.RequestIdentityChangeAsync(
            new RequestStudioIdentityChangeCommand(
                ActorUserId: harness.IndieOwnerUserId,
                StudioId: studioId,
                ProposedName: "Monsoon Pixel Studios Pvt Ltd",
                ProposedSlug: "monsoon-pixel-studios",
                ProposedGstin: "29ABCDE1234F1Z5",
                ProofUrls: ["https://monsoonpixel.in/incorporation"],
                ApplicantNotes: "Re-submitted incorporation & GSTIN update."));
        Assert.True(reStaged.IsSuccess, reStaged.Error.Message);

        // Admin Pending queue surfaces the staged identity change item alongside initial pending items.
        var queueWithIdentityChange = await harness.VerificationService.GetAdminQueueAsync(StudioVerificationStatus.Pending);
        Assert.Equal(1, queueWithIdentityChange.Counts.PendingCount);
        Assert.Equal(0, queueWithIdentityChange.Counts.PendingInitialCount);
        Assert.Equal(1, queueWithIdentityChange.Counts.PendingIdentityChangeCount);
        var stagedQueueItem = Assert.Single(queueWithIdentityChange.Items);
        Assert.True(stagedQueueItem.HasPendingIdentityChange);
        Assert.Equal("Monsoon Pixel Studios Pvt Ltd", stagedQueueItem.PendingIdentityChange!.ProposedName);

        // If Admin rejects the staged identity change, live Verified badge & original identity stay 100% intact!
        var rejectedIdentity = await harness.VerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: harness.AdminUserId,
                StudioId: studioId,
                Decision: StudioVerificationDecision.RejectIdentityChange,
                Reason: "Incorporation URL returned 403; asked owner for public MCA/Steam publisher update.",
                ReviewerFeedback: "Your live verified studio remains active, but the incorporation proof link was inaccessible. Please attach a public proof URL and re-submit."));
        Assert.True(rejectedIdentity.IsSuccess, rejectedIdentity.Error.Message);
        Assert.Equal(StudioVerificationStatus.Verified, rejectedIdentity.Value.NewStatus);
        Assert.Equal("Monsoon Pixel Collective", rejectedIdentity.Value.StudioName);
        Assert.Contains(harness.EmailSender.Sent, e => e.Tag == "studio_identity_change_rejected");

        // Owner re-submits with updated proof -> Admin approves -> Name, Slug, and GSTIN swap atomically!
        var finalStaged = await harness.VerificationService.RequestIdentityChangeAsync(
            new RequestStudioIdentityChangeCommand(
                ActorUserId: harness.IndieOwnerUserId,
                StudioId: studioId,
                ProposedName: "Monsoon Pixel Studios Pvt Ltd",
                ProposedSlug: "monsoon-pixel-studios",
                ProposedGstin: "29ABCDE1234F1Z5",
                ProofUrls: ["https://store.steampowered.com/publisher/monsoonpixelstudios"],
                ApplicantNotes: "Updated Steam publisher page showing Monsoon Pixel Studios Pvt Ltd."));
        Assert.True(finalStaged.IsSuccess, finalStaged.Error.Message);

        var approvedIdentity = await harness.VerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: harness.AdminUserId,
                StudioId: studioId,
                Decision: StudioVerificationDecision.ApproveIdentityChange,
                Reason: "Verified updated Steam publisher legal entity + valid Karnataka GSTIN.",
                ReviewerFeedback: "Legal entity name, studio slug, and GSTIN are now updated."));
        Assert.True(approvedIdentity.IsSuccess, approvedIdentity.Error.Message);
        Assert.Equal(StudioVerificationStatus.Verified, approvedIdentity.Value.NewStatus);
        Assert.Equal("Monsoon Pixel Studios Pvt Ltd", approvedIdentity.Value.StudioName);
        Assert.Equal("monsoon-pixel-studios", approvedIdentity.Value.StudioSlug);
        Assert.Contains(harness.EmailSender.Sent, e => e.Tag == "studio_identity_change_approved");

        var oldSlugAnon = await harness.ProfileService.GetPublicStudioBySlugAsync(
            "monsoon-pixel",
            new PublicStudioViewer(IsAuthenticated: false, UserId: null, IsAdmin: false));
        Assert.True(oldSlugAnon.IsFailure);

        var newSlugAnon = await harness.ProfileService.GetPublicStudioBySlugAsync(
            "monsoon-pixel-studios",
            new PublicStudioViewer(IsAuthenticated: false, UserId: null, IsAdmin: false));
        Assert.True(newSlugAnon.IsSuccess, newSlugAnon.Error.Message);
        Assert.Equal("Monsoon Pixel Studios Pvt Ltd", newSlugAnon.Value.Name);

        // Revocation must also pause published jobs atomically, without extending validity.
        var publishedAt = harness.Clock.UtcNow;
        var publishedJobId = Guid.NewGuid();
        harness.DbContext.Jobs.Add(new Job
        {
            Id = publishedJobId, StudioId = studioId, PostedByUserId = harness.IndieOwnerUserId,
            Slug = "revocation-regression", Status = JobStatus.Active,
            FirstPublishedAt = publishedAt, PublishedAt = publishedAt, ExpiresAt = publishedAt.AddYears(1)
        });
        await harness.DbContext.SaveChangesAsync();

        // 11. Admin can Revoke a verified studio with mandatory audit reason + studio-facing explanation.
        var revoked = await harness.VerificationService.ReviewVerificationAsync(
            new ReviewStudioVerificationCommand(
                AdminUserId: harness.AdminUserId,
                StudioId: studioId,
                Decision: StudioVerificationDecision.Revoke,
                Reason: "Domain ownership lapsed; pausing verification pending re-check.",
                ReviewerFeedback: "Your studio domain monsoonpixel.in is no longer resolving. Please update your website or proof links and resubmit."));
        Assert.True(revoked.IsSuccess, revoked.Error.Message);
        Assert.Equal(StudioVerificationStatus.Rejected, revoked.Value.NewStatus);
        var pausedJob = await harness.DbContext.Jobs.AsNoTracking().SingleAsync(j => j.Id == publishedJobId);
        Assert.Equal(JobStatus.Paused, pausedJob.Status);
        Assert.Equal(publishedAt.AddYears(1), pausedJob.ExpiresAt);
        var revocationHistory = await harness.DbContext.JobStatusHistory.SingleAsync(j => j.JobId == publishedJobId);
        Assert.Equal("verification_revoked", revocationHistory.ReasonCode);
        Assert.Equal(harness.AdminUserId, revocationHistory.ActorUserId);
    }

    private sealed class VerificationTestHarness : IAsyncDisposable
    {
        public AppDbContext DbContext { get; }
        public FixedClock Clock { get; }
        public AuditLogger AuditLogger { get; }
        public RecordingEmailSender EmailSender { get; }
        public StudioProfileService ProfileService { get; }
        public StudioTeamService TeamService { get; }
        public StudioVerificationService VerificationService { get; }
        public Guid AdminUserId { get; }
        public Guid IndieOwnerUserId { get; }
        public Guid RecruiterUserId { get; }
        public int BengaluruCityId { get; }
        public int GodotSkillId { get; }

        private VerificationTestHarness(
            AppDbContext dbContext,
            FixedClock clock,
            AuditLogger auditLogger,
            RecordingEmailSender emailSender,
            StudioProfileService profileService,
            StudioTeamService teamService,
            StudioVerificationService verificationService,
            Guid adminUserId,
            Guid indieOwnerUserId,
            Guid recruiterUserId,
            int bengaluruCityId,
            int godotSkillId)
        {
            DbContext = dbContext;
            Clock = clock;
            AuditLogger = auditLogger;
            EmailSender = emailSender;
            ProfileService = profileService;
            TeamService = teamService;
            VerificationService = verificationService;
            AdminUserId = adminUserId;
            IndieOwnerUserId = indieOwnerUserId;
            RecruiterUserId = recruiterUserId;
            BengaluruCityId = bengaluruCityId;
            GodotSkillId = godotSkillId;
        }

        public static async Task<VerificationTestHarness> CreateAsync()
        {
            var clock = new FixedClock();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"studio-verification-tests-{Guid.NewGuid():N}")
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

            dbContext.Skills.Add(new Skill
            {
                Id = 105,
                Name = "Godot",
                Slug = "godot",
                Category = SkillCategory.Engine,
                IsActive = true
            });

            var adminUserId = Guid.NewGuid();
            var unconfirmedAdminId = Guid.NewGuid();
            var indieOwnerUserId = Guid.NewGuid();
            var recruiterUserId = Guid.NewGuid();

            dbContext.Users.AddRange(
                new ApplicationUser
                {
                    Id = adminUserId,
                    UserName = "admin@hiregamedev.com",
                    NormalizedUserName = "ADMIN@HIREGAMEDEV.COM",
                    Email = "admin@hiregamedev.com",
                    NormalizedEmail = "ADMIN@HIREGAMEDEV.COM",
                    EmailConfirmed = true,
                    DisplayName = "Platform Reviewer",
                    AccountType = AccountType.Studio,
                    IsActive = true
                },
                new ApplicationUser
                {
                    Id = unconfirmedAdminId,
                    UserName = "unconfirmed-admin@hiregamedev.com",
                    NormalizedUserName = "UNCONFIRMED-ADMIN@HIREGAMEDEV.COM",
                    Email = "unconfirmed-admin@hiregamedev.com",
                    NormalizedEmail = "UNCONFIRMED-ADMIN@HIREGAMEDEV.COM",
                    EmailConfirmed = false,
                    DisplayName = "Unconfirmed Admin",
                    AccountType = AccountType.Studio,
                    IsActive = true
                },
                new ApplicationUser
                {
                    Id = indieOwnerUserId,
                    UserName = "indie.founder@gmail.com",
                    NormalizedUserName = "INDIE.FOUNDER@GMAIL.COM",
                    Email = "indie.founder@gmail.com",
                    NormalizedEmail = "INDIE.FOUNDER@GMAIL.COM",
                    EmailConfirmed = true,
                    DisplayName = "Aarav Rao",
                    AccountType = AccountType.Studio,
                    IsActive = true
                },
                new ApplicationUser
                {
                    Id = recruiterUserId,
                    UserName = "recruiter@monsoonpixel.in",
                    NormalizedUserName = "RECRUITER@MONSOONPIXEL.IN",
                    Email = "recruiter@monsoonpixel.in",
                    NormalizedEmail = "RECRUITER@MONSOONPIXEL.IN",
                    EmailConfirmed = true,
                    DisplayName = "Meera Nair",
                    AccountType = AccountType.Studio,
                    IsActive = true
                });

            await dbContext.SaveChangesAsync();

            var auditLogger = new AuditLogger(dbContext, new NullIpAccessor(), clock);
            var emailSender = new RecordingEmailSender();
            var fakeStorage = new MinimalPublicStorageFake();
            var profileService = new StudioProfileService(dbContext, fakeStorage, auditLogger, clock);
            var teamService = new StudioTeamService(dbContext, auditLogger, clock);
            var verificationService = new StudioVerificationService(
                dbContext,
                fakeStorage,
                auditLogger,
                emailSender,
                clock,
                NullLogger<StudioVerificationService>.Instance);

            return new VerificationTestHarness(
                dbContext,
                clock,
                auditLogger,
                emailSender,
                profileService,
                teamService,
                verificationService,
                adminUserId,
                indieOwnerUserId,
                recruiterUserId,
                bengaluruCityId: 1,
                godotSkillId: 105);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class MinimalPublicStorageFake : IFileStorageService
    {
        public string ProviderName => "TestStorage";

        public Task PutAsync(FileStoragePutRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<StoredObjectReadResult?> OpenReadAsync(
            string objectKey,
            StorageVisibility visibility,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredObjectReadResult?>(null);

        public Task<bool> DeleteIfExistsAsync(
            string objectKey,
            StorageVisibility visibility,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public string ResolvePublicUrl(string objectKey) =>
            ObjectKeyRules.TryNormalize(objectKey, out var normalized, out _)
                ? $"https://cdn.hiregamedev.test/{normalized}"
                : throw new ArgumentException("Invalid key", nameof(objectKey));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class NullIpAccessor : IClientIpAccessor
    {
        public IPAddress? GetClientIpAddress() => null;
    }
}
