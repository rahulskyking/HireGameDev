using System.Net;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Email;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireGameDev.Infrastructure.Services;

internal sealed class StudioVerificationService : IStudioVerificationService
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogger _auditLogger;
    private readonly IEmailSender _emailSender;
    private readonly IClock _clock;
    private readonly ILogger<StudioVerificationService> _logger;

    public StudioVerificationService(
        AppDbContext dbContext,
        IFileStorageService fileStorage,
        IAuditLogger auditLogger,
        IEmailSender emailSender,
        IClock clock,
        ILogger<StudioVerificationService> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _auditLogger = auditLogger;
        _emailSender = emailSender;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<StudioVerificationWorkspaceModel>> GetVerificationWorkspaceAsync(
        Guid actorUserId,
        Guid? requestedStudioId = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Unauthorized("Studio.Unauthorized", "Sign in to view studio verification."));
        }

        var membershipRows = await (
            from m in _dbContext.StudioMembers.AsNoTracking()
            join s in _dbContext.StudioProfiles.AsNoTracking() on m.StudioId equals s.Id
            where m.UserId == actorUserId && s.DeletedAt == null
            select new { m.StudioId, m.MemberRole, s.CreatedAt }
        ).ToListAsync(cancellationToken);

        if (membershipRows.Count == 0)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.NotFound("Studio.NotOnboarded", "Complete studio onboarding before requesting verification."));
        }

        var orderedMemberships = membershipRows
            .OrderBy(r => r.MemberRole switch
            {
                StudioMemberRole.Owner => 0,
                StudioMemberRole.Recruiter => 1,
                _ => 2
            })
            .ThenBy(r => r.CreatedAt)
            .ToList();

        var chosen = requestedStudioId.HasValue && requestedStudioId.Value != Guid.Empty
            ? orderedMemberships.FirstOrDefault(m => m.StudioId == requestedStudioId.Value)
            : orderedMemberships[0];

        if (chosen is null)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Forbidden("Studio.Forbidden", "You are not a member of the requested studio workspace."));
        }

        return await BuildWorkspaceModelAsync(chosen.StudioId, chosen.MemberRole, cancellationToken);
    }

    public async Task<Result<StudioVerificationWorkspaceModel>> SubmitForVerificationAsync(
        SubmitStudioVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActorUserId == Guid.Empty || command.StudioId == Guid.Empty)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidIdentity", "Valid studio and user identifiers are required."));
        }

        var studio = await _dbContext.StudioProfiles
            .FirstOrDefaultAsync(s => s.Id == command.StudioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.NotFound("Studio.NotFound", "Studio workspace was not found."));
        }

        var callerMember = await _dbContext.StudioMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == command.ActorUserId,
                cancellationToken);

        if (callerMember is null || !StudioProfileRules.CanSubmitVerification(callerMember.MemberRole))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Forbidden(
                    "StudioVerification.OwnerOnly",
                    "Only the studio Owner can submit or resubmit a studio verification request."));
        }

        if (studio.VerificationStatus == StudioVerificationStatus.Verified)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Conflict(
                    "StudioVerification.AlreadyVerified",
                    "This studio workspace is already verified. Use the Controlled Identity Update form to request a Name, Slug, or GSTIN change."));
        }

        if (studio.VerificationStatus == StudioVerificationStatus.Pending)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Conflict(
                    "StudioVerification.AlreadyPending",
                    "A verification request is already pending admin review."));
        }

        string? normalizedWebsite = null;
        if (!string.IsNullOrWhiteSpace(command.Website))
        {
            if (!ExternalUrlRules.TryNormalizeHttpsUrl(command.Website, out var canonicalWebsite, out var websiteError))
            {
                return Result.Failure<StudioVerificationWorkspaceModel>(
                    Error.Validation("StudioVerification.InvalidWebsite", $"Studio website: {websiteError}"));
            }

            normalizedWebsite = canonicalWebsite;
        }

        if (!StudioProfileRules.TryNormalizeGstin(command.Gstin, out var normalizedGstin, out var gstinError))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidGstin", gstinError!));
        }

        if (!StudioVerificationRules.TryNormalizeProofUrls(command.ProofUrls, out var normalizedProofUrls, out var proofUrlError))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidProofUrls", proofUrlError!));
        }

        if (!StudioVerificationRules.TryNormalizeApplicantNotes(command.ApplicantNotes, out var normalizedApplicantNotes, out var notesError))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidApplicantNotes", notesError!));
        }

        var engineCount = await _dbContext.StudioSkills
            .AsNoTracking()
            .CountAsync(
                ss => ss.StudioId == studio.Id &&
                      ss.Skill.IsActive &&
                      ss.Skill.Category == SkillCategory.Engine,
                cancellationToken);

        var shippedStoreUrlCount = await _dbContext.StudioTitles
            .AsNoTracking()
            .CountAsync(
                t => t.StudioId == studio.Id && t.StoreUrl != null && t.StoreUrl != string.Empty,
                cancellationToken);

        var readiness = StudioVerificationRules.EvaluateReadiness(
            studioName: studio.Name,
            description: studio.Description,
            cityId: studio.CityId,
            engineSkillCount: engineCount,
            normalizedWebsiteUrl: normalizedWebsite,
            normalizedGstin: normalizedGstin,
            shippedTitleStoreUrlCount: shippedStoreUrlCount,
            proofUrlCount: normalizedProofUrls.Length);

        if (!readiness.CanSubmit)
        {
            var unmet = readiness.Items
                .Where(i => !i.IsMet)
                .Select(i => i.Detail)
                .ToArray();

            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation(
                    "StudioVerification.PrerequisitesNotMet",
                    string.Join(" ", unmet)));
        }

        var owner = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == studio.OwnerUserId, cancellationToken);

        var domainAssessment = StudioVerificationRules.AssessEmailDomain(owner?.Email, normalizedWebsite);
        var previousStatus = studio.VerificationStatus;
        var now = _clock.UtcNow;

        studio.Website = normalizedWebsite;
        studio.CompanyEmailDomain = StudioProfileRules.ExtractCompanyDomainFromWebsite(normalizedWebsite);
        studio.Gstin = normalizedGstin;
        studio.VerificationStatus = StudioVerificationStatus.Pending;
        studio.VerificationSubmittedAt = now;
        studio.VerificationProofUrls = normalizedProofUrls;
        studio.VerificationApplicantNotes = normalizedApplicantNotes;

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "studio.verification.submitted",
            EntityType: "studio_profile",
            EntityId: studio.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.ActorUserId,
            BeforeData: new { Status = previousStatus.ToString().ToLowerInvariant() },
            AfterData: new
            {
                Status = "pending",
                DomainSignal = domainAssessment.Signal.ToString(),
                HasGstin = !string.IsNullOrWhiteSpace(normalizedGstin),
                HasWebsite = !string.IsNullOrWhiteSpace(normalizedWebsite),
                ProofUrlCount = normalizedProofUrls.Length
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
        {
            await TrySendSubmissionEmailAsync(
                owner,
                studio.Name,
                studio.Slug,
                isIdentityChange: false,
                cancellationToken);
        }

        return await BuildWorkspaceModelAsync(studio.Id, callerMember.MemberRole, cancellationToken);
    }

    public async Task<Result<StudioVerificationWorkspaceModel>> RequestIdentityChangeAsync(
        RequestStudioIdentityChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActorUserId == Guid.Empty || command.StudioId == Guid.Empty)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidIdentity", "Valid studio and user identifiers are required."));
        }

        var studio = await _dbContext.StudioProfiles
            .FirstOrDefaultAsync(s => s.Id == command.StudioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.NotFound("Studio.NotFound", "Studio workspace was not found."));
        }

        var callerMember = await _dbContext.StudioMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == command.ActorUserId,
                cancellationToken);

        if (callerMember is null || !StudioProfileRules.CanSubmitVerification(callerMember.MemberRole))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Forbidden(
                    "StudioVerification.OwnerOnly",
                    "Only the studio Owner can request a verified identity update."));
        }

        if (studio.VerificationStatus != StudioVerificationStatus.Verified)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Conflict(
                    "StudioVerification.NotVerified",
                    "Controlled identity re-verification is for verified studios. While unverified or rejected, you can edit Studio Name, Slug, and GSTIN directly on your Studio Profile."));
        }

        if (studio.PendingIdentityRequestedAt.HasValue)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Conflict(
                    "StudioVerification.IdentityChangeAlreadyPending",
                    "An identity re-verification request is already pending admin review. Cancel the pending request first if you need to revise it."));
        }

        if (!StudioVerificationRules.TryValidateIdentityChangeRequest(
                currentName: studio.Name,
                currentSlug: studio.Slug,
                currentGstin: studio.Gstin,
                rawProposedName: command.ProposedName,
                rawProposedSlug: command.ProposedSlug,
                rawProposedGstin: command.ProposedGstin,
                out var proposedName,
                out var proposedSlug,
                out var proposedGstin,
                out var identityError))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidIdentityChange", identityError!));
        }

        if (!StudioVerificationRules.TryNormalizeProofUrls(command.ProofUrls, out var normalizedProofUrls, out var proofUrlError))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidProofUrls", proofUrlError!));
        }

        if (!StudioVerificationRules.TryNormalizeApplicantNotes(command.ApplicantNotes, out var normalizedNotes, out var notesError))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidApplicantNotes", notesError!));
        }

        if (normalizedNotes is null || normalizedNotes.Length < StudioVerificationRules.MinIdentityChangeNotesLength)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation(
                    "StudioVerification.IdentityNotesRequired",
                    $"Briefly explain why your verified identity is changing (at least {StudioVerificationRules.MinIdentityChangeNotesLength} characters — e.g. legal incorporation, GSTIN registration, or studio rebrand)."));
        }

        var slugChanged = !string.Equals(studio.Slug, proposedSlug, StringComparison.OrdinalIgnoreCase);
        if (slugChanged)
        {
            var slugTaken = await IsSlugTakenOrReservedAsync(proposedSlug, studio.Id, cancellationToken);
            if (slugTaken)
            {
                return Result.Failure<StudioVerificationWorkspaceModel>(
                    Error.Conflict(
                        "StudioProfile.SlugTaken",
                        $"The proposed URL slug '{proposedSlug}' is already in use or reserved."));
            }
        }

        var now = _clock.UtcNow;
        studio.PendingIdentityName = proposedName;
        studio.PendingIdentitySlug = proposedSlug;
        studio.PendingIdentityGstin = proposedGstin;
        studio.PendingIdentityRequestedAt = now;
        studio.VerificationProofUrls = normalizedProofUrls;
        studio.VerificationApplicantNotes = normalizedNotes;

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "studio.identity_change.requested",
            EntityType: "studio_profile",
            EntityId: studio.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.ActorUserId,
            BeforeData: new { studio.Name, studio.Slug, studio.Gstin },
            AfterData: new
            {
                ProposedName = proposedName,
                ProposedSlug = proposedSlug,
                ProposedGstin = proposedGstin,
                ProofUrlCount = normalizedProofUrls.Length
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var owner = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == studio.OwnerUserId, cancellationToken);

        if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
        {
            await TrySendSubmissionEmailAsync(
                owner,
                studio.Name,
                studio.Slug,
                isIdentityChange: true,
                cancellationToken);
        }

        return await BuildWorkspaceModelAsync(studio.Id, callerMember.MemberRole, cancellationToken);
    }

    public async Task<Result<StudioVerificationWorkspaceModel>> CancelIdentityChangeAsync(
        CancelStudioIdentityChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ActorUserId == Guid.Empty || command.StudioId == Guid.Empty)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Validation("StudioVerification.InvalidIdentity", "Valid studio and user identifiers are required."));
        }

        var studio = await _dbContext.StudioProfiles
            .FirstOrDefaultAsync(s => s.Id == command.StudioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.NotFound("Studio.NotFound", "Studio workspace was not found."));
        }

        var callerMember = await _dbContext.StudioMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == command.ActorUserId,
                cancellationToken);

        if (callerMember is null || !StudioProfileRules.CanSubmitVerification(callerMember.MemberRole))
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Forbidden(
                    "StudioVerification.OwnerOnly",
                    "Only the studio Owner can cancel a pending identity update request."));
        }

        if (!studio.PendingIdentityRequestedAt.HasValue)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.Conflict(
                    "StudioVerification.NoPendingIdentityChange",
                    "There is no pending identity re-verification request to cancel."));
        }

        var cancelledProposal = new
        {
            studio.PendingIdentityName,
            studio.PendingIdentitySlug,
            studio.PendingIdentityGstin
        };

        ClearPendingIdentityFields(studio);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "studio.identity_change.cancelled",
            EntityType: "studio_profile",
            EntityId: studio.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.ActorUserId,
            BeforeData: cancelledProposal));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildWorkspaceModelAsync(studio.Id, callerMember.MemberRole, cancellationToken);
    }

    public async Task<AdminStudioVerificationQueueModel> GetAdminQueueAsync(
        StudioVerificationStatus? statusFilter = StudioVerificationStatus.Pending,
        CancellationToken cancellationToken = default)
    {
        var activeStudiosQuery = _dbContext.StudioProfiles
            .AsNoTracking()
            .Where(s => s.DeletedAt == null);

        var statusGroupCounts = await activeStudiosQuery
            .GroupBy(s => s.VerificationStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var pendingInitialCount = statusGroupCounts.FirstOrDefault(x => x.Status == StudioVerificationStatus.Pending)?.Count ?? 0;
        var verifiedCount = statusGroupCounts.FirstOrDefault(x => x.Status == StudioVerificationStatus.Verified)?.Count ?? 0;
        var rejectedCount = statusGroupCounts.FirstOrDefault(x => x.Status == StudioVerificationStatus.Rejected)?.Count ?? 0;
        var unverifiedCount = statusGroupCounts.FirstOrDefault(x => x.Status == StudioVerificationStatus.Unverified)?.Count ?? 0;

        var pendingIdentityChangeCount = await activeStudiosQuery
            .CountAsync(
                s => s.VerificationStatus == StudioVerificationStatus.Verified &&
                     s.PendingIdentityRequestedAt != null,
                cancellationToken);

        var counts = new AdminStudioVerificationQueueCounts(
            PendingCount: pendingInitialCount + pendingIdentityChangeCount,
            PendingInitialCount: pendingInitialCount,
            PendingIdentityChangeCount: pendingIdentityChangeCount,
            VerifiedCount: verifiedCount,
            RejectedCount: rejectedCount,
            UnverifiedCount: unverifiedCount,
            TotalCount: pendingInitialCount + verifiedCount + rejectedCount + unverifiedCount);

        IQueryable<StudioProfile> filteredQuery;
        if (!statusFilter.HasValue)
        {
            filteredQuery = activeStudiosQuery;
        }
        else if (statusFilter.Value == StudioVerificationStatus.Pending)
        {
            // Surface both initial/resubmitted Pending verifications AND Verified studios with staged identity changes.
            filteredQuery = activeStudiosQuery.Where(
                s => s.VerificationStatus == StudioVerificationStatus.Pending ||
                     (s.VerificationStatus == StudioVerificationStatus.Verified && s.PendingIdentityRequestedAt != null));
        }
        else
        {
            filteredQuery = activeStudiosQuery.Where(s => s.VerificationStatus == statusFilter.Value);
        }

        var studios = await filteredQuery
            .Include(s => s.City)
            .ToListAsync(cancellationToken);

        if (studios.Count == 0)
        {
            return new AdminStudioVerificationQueueModel(
                ActiveFilter: statusFilter,
                Counts: counts,
                Items: Array.Empty<AdminStudioVerificationQueueItem>());
        }

        var studioIds = studios.Select(s => s.Id).ToArray();
        var userIds = studios
            .Select(s => s.OwnerUserId)
            .Concat(studios.Where(s => s.VerifiedBy.HasValue).Select(s => s.VerifiedBy!.Value))
            .Distinct()
            .ToArray();

        var usersById = await _dbContext.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var titlesByStudio = (await _dbContext.StudioTitles
            .AsNoTracking()
            .Where(t => studioIds.Contains(t.StudioId))
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.ReleaseYear)
            .Select(t => new
            {
                t.StudioId,
                Item = new StudioTitleItem(
                    t.Id,
                    t.GameName,
                    t.ReleaseYear,
                    t.Platforms,
                    t.StoreUrl,
                    t.IgdbId,
                    t.SortOrder)
            })
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.StudioId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StudioTitleItem>)g.Select(x => x.Item).ToArray());

        var enginesByStudio = (await _dbContext.StudioSkills
            .AsNoTracking()
            .Where(ss => studioIds.Contains(ss.StudioId) && ss.Skill.IsActive && ss.Skill.Category == SkillCategory.Engine)
            .OrderBy(ss => ss.Skill.Name)
            .Select(ss => new { ss.StudioId, ss.Skill.Name })
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.StudioId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).ToArray());

        var now = _clock.UtcNow;

        var orderedStudios = statusFilter == StudioVerificationStatus.Pending
            ? studios.OrderBy(s => s.PendingIdentityRequestedAt ?? s.VerificationSubmittedAt ?? s.UpdatedAt).ToList()
            : studios.OrderByDescending(s => s.PendingIdentityRequestedAt ?? s.VerificationSubmittedAt ?? s.UpdatedAt).ToList();

        var items = new List<AdminStudioVerificationQueueItem>(orderedStudios.Count);
        foreach (var s in orderedStudios)
        {
            usersById.TryGetValue(s.OwnerUserId, out var ownerInfo);
            var verifierEmail = s.VerifiedBy.HasValue && usersById.TryGetValue(s.VerifiedBy.Value, out var verifierInfo)
                ? verifierInfo.Email
                : null;

            var ownerEmail = ownerInfo?.Email ?? string.Empty;
            var ownerName = !string.IsNullOrWhiteSpace(ownerInfo?.DisplayName)
                ? ownerInfo.DisplayName
                : ownerEmail;

            var domainAssessment = StudioVerificationRules.AssessEmailDomain(ownerEmail, s.Website);
            var pendingIdentity = BuildPendingIdentityChangeOrNull(s);
            var hasPendingIdentity = pendingIdentity is not null;

            double? waitingHours = null;
            var isPastSla = false;
            if (s.VerificationStatus == StudioVerificationStatus.Pending || hasPendingIdentity)
            {
                var submittedRef = s.PendingIdentityRequestedAt ?? s.VerificationSubmittedAt ?? s.UpdatedAt;
                waitingHours = Math.Max(0, Math.Round((now - submittedRef).TotalHours, 1));
                isPastSla = waitingHours.Value >= StudioVerificationRules.TargetReviewSlaHours;
            }

            items.Add(new AdminStudioVerificationQueueItem(
                StudioId: s.Id,
                StudioName: s.Name,
                StudioSlug: s.Slug,
                LogoUrl: ResolveLogoUrlOrNull(s.LogoPath),
                StudioType: s.StudioType,
                SizeBand: s.SizeBand,
                CityName: s.City?.Name,
                CityState: s.City?.State,
                OwnerUserId: s.OwnerUserId,
                OwnerDisplayName: ownerName,
                OwnerEmail: ownerEmail,
                DomainAssessment: domainAssessment,
                Website: s.Website,
                Gstin: s.Gstin,
                ProofUrls: s.VerificationProofUrls,
                ApplicantNotes: s.VerificationApplicantNotes,
                HasPendingIdentityChange: hasPendingIdentity,
                PendingIdentityChange: pendingIdentity,
                Titles: titlesByStudio.TryGetValue(s.Id, out var tList) ? tList : Array.Empty<StudioTitleItem>(),
                EngineNames: enginesByStudio.TryGetValue(s.Id, out var eList) ? eList : Array.Empty<string>(),
                VerificationStatus: s.VerificationStatus,
                VerificationSubmittedAt: s.VerificationSubmittedAt,
                WaitingHours: waitingHours,
                IsPastTargetSla: isPastSla,
                VerifiedAt: s.VerifiedAt,
                VerifiedByEmail: verifierEmail,
                ReviewerFeedback: s.VerificationNotes,
                UpdatedAt: s.UpdatedAt));
        }

        return new AdminStudioVerificationQueueModel(
            ActiveFilter: statusFilter,
            Counts: counts,
            Items: items);
    }

    public async Task<Result<ReviewStudioVerificationResult>> ReviewVerificationAsync(
        ReviewStudioVerificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AdminUserId == Guid.Empty || command.StudioId == Guid.Empty)
        {
            return Result.Failure<ReviewStudioVerificationResult>(
                Error.Validation("StudioVerification.InvalidIdentity", "Valid admin and studio identifiers are required."));
        }

        var isAdmin = await IsActiveAdminUserAsync(command.AdminUserId, cancellationToken);
        if (!isAdmin)
        {
            return Result.Failure<ReviewStudioVerificationResult>(
                Error.Forbidden("StudioVerification.AdminRequired", "Admin privileges are required to review studio verification."));
        }

        var trimmedReason = command.Reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length < StudioVerificationRules.MinAdminReasonLength ||
            trimmedReason.Length > StudioVerificationRules.MaxAdminReasonLength)
        {
            return Result.Failure<ReviewStudioVerificationResult>(
                Error.Validation(
                    "StudioVerification.ReasonRequired",
                    $"Enter an internal admin audit reason between {StudioVerificationRules.MinAdminReasonLength} and {StudioVerificationRules.MaxAdminReasonLength} characters."));
        }

        var trimmedFeedback = string.IsNullOrWhiteSpace(command.ReviewerFeedback)
            ? null
            : command.ReviewerFeedback.Trim();

        if (trimmedFeedback is not null && trimmedFeedback.Length > StudioVerificationRules.MaxReviewerNotesLength)
        {
            return Result.Failure<ReviewStudioVerificationResult>(
                Error.Validation(
                    "StudioVerification.FeedbackTooLong",
                    $"Studio-facing reviewer notes must be {StudioVerificationRules.MaxReviewerNotesLength} characters or fewer."));
        }

        var requiresRejectionFeedback = command.Decision is
            StudioVerificationDecision.Reject or
            StudioVerificationDecision.Revoke or
            StudioVerificationDecision.RejectIdentityChange;

        if (requiresRejectionFeedback &&
            (trimmedFeedback is null || trimmedFeedback.Length < StudioVerificationRules.MinRejectionFeedbackLength))
        {
            return Result.Failure<ReviewStudioVerificationResult>(
                Error.Validation(
                    "StudioVerification.FeedbackRequired",
                    $"Provide clear studio-facing feedback (at least {StudioVerificationRules.MinRejectionFeedbackLength} characters) explaining what needs to be fixed or why verification was revoked."));
        }

        var studio = await _dbContext.StudioProfiles
            .FirstOrDefaultAsync(s => s.Id == command.StudioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<ReviewStudioVerificationResult>(
                Error.NotFound("Studio.NotFound", "Studio workspace was not found."));
        }

        var previousStatus = studio.VerificationStatus;
        var now = _clock.UtcNow;
        StudioVerificationStatus targetStatus;
        string auditAction;
        object beforeData;
        object afterData;

        switch (command.Decision)
        {
            case StudioVerificationDecision.Approve:
                if (previousStatus == StudioVerificationStatus.Verified)
                {
                    return Result.Failure<ReviewStudioVerificationResult>(
                        Error.Conflict(
                            "StudioVerification.AlreadyVerified",
                            "Studio is already verified. Use ApproveIdentityChange if reviewing a pending identity update."));
                }

                targetStatus = StudioVerificationStatus.Verified;
                studio.VerificationStatus = targetStatus;
                studio.VerifiedAt = now;
                studio.VerifiedBy = command.AdminUserId;
                studio.VerificationNotes = trimmedFeedback;
                ClearPendingIdentityFields(studio);
                auditAction = "admin.studio_verification.approve";
                beforeData = new { Status = previousStatus.ToString().ToLowerInvariant() };
                afterData = new { Status = "verified", ReviewerFeedback = trimmedFeedback };
                break;

            case StudioVerificationDecision.Reject:
                if (previousStatus == StudioVerificationStatus.Verified)
                {
                    return Result.Failure<ReviewStudioVerificationResult>(
                        Error.Conflict(
                            "StudioVerification.UseRevokeForVerified",
                            "Use RejectIdentityChange for a staged identity update or Revoke to remove verification from a verified studio."));
                }

                targetStatus = StudioVerificationStatus.Rejected;
                studio.VerificationStatus = targetStatus;
                studio.VerifiedAt = null;
                studio.VerifiedBy = null;
                studio.VerificationNotes = trimmedFeedback;
                ClearPendingIdentityFields(studio);
                auditAction = "admin.studio_verification.reject";
                beforeData = new { Status = previousStatus.ToString().ToLowerInvariant() };
                afterData = new { Status = "rejected", ReviewerFeedback = trimmedFeedback };
                break;

            case StudioVerificationDecision.ApproveIdentityChange:
                if (previousStatus != StudioVerificationStatus.Verified ||
                    !studio.PendingIdentityRequestedAt.HasValue ||
                    string.IsNullOrWhiteSpace(studio.PendingIdentityName) ||
                    string.IsNullOrWhiteSpace(studio.PendingIdentitySlug))
                {
                    return Result.Failure<ReviewStudioVerificationResult>(
                        Error.Conflict(
                            "StudioVerification.NoPendingIdentityChange",
                            "This studio does not have a pending identity re-verification request to approve."));
                }

                var proposedName = studio.PendingIdentityName;
                var proposedSlug = studio.PendingIdentitySlug;
                var proposedGstin = studio.PendingIdentityGstin;

                if (!string.Equals(studio.Slug, proposedSlug, StringComparison.OrdinalIgnoreCase))
                {
                    var slugTaken = await IsSlugTakenOrReservedAsync(proposedSlug, studio.Id, cancellationToken);
                    if (slugTaken)
                    {
                        return Result.Failure<ReviewStudioVerificationResult>(
                            Error.Conflict(
                                "StudioProfile.SlugTaken",
                                $"Cannot approve identity change because URL slug '{proposedSlug}' is already in use."));
                    }
                }

                beforeData = new
                {
                    Status = "verified",
                    studio.Name,
                    studio.Slug,
                    studio.Gstin
                };

                targetStatus = StudioVerificationStatus.Verified;
                studio.Name = proposedName;
                studio.Slug = proposedSlug;
                studio.Gstin = proposedGstin;
                studio.VerifiedAt = now;
                studio.VerifiedBy = command.AdminUserId;
                studio.VerificationNotes = trimmedFeedback;
                ClearPendingIdentityFields(studio);

                auditAction = "admin.studio_identity_change.approve";
                afterData = new
                {
                    Status = "verified",
                    Name = proposedName,
                    Slug = proposedSlug,
                    Gstin = proposedGstin,
                    ReviewerFeedback = trimmedFeedback
                };
                break;

            case StudioVerificationDecision.RejectIdentityChange:
                if (previousStatus != StudioVerificationStatus.Verified || !studio.PendingIdentityRequestedAt.HasValue)
                {
                    return Result.Failure<ReviewStudioVerificationResult>(
                        Error.Conflict(
                            "StudioVerification.NoPendingIdentityChange",
                            "This studio does not have a pending identity re-verification request to reject."));
                }

                beforeData = new
                {
                    Status = "verified",
                    LiveName = studio.Name,
                    LiveSlug = studio.Slug,
                    LiveGstin = studio.Gstin,
                    RejectedProposedName = studio.PendingIdentityName,
                    RejectedProposedSlug = studio.PendingIdentitySlug,
                    RejectedProposedGstin = studio.PendingIdentityGstin
                };

                // Preserve live Verified status and verified Name/Slug/GSTIN intact!
                targetStatus = StudioVerificationStatus.Verified;
                studio.VerificationNotes = trimmedFeedback;
                ClearPendingIdentityFields(studio);

                auditAction = "admin.studio_identity_change.reject";
                afterData = new
                {
                    Status = "verified",
                    studio.Name,
                    studio.Slug,
                    studio.Gstin,
                    ReviewerFeedback = trimmedFeedback
                };
                break;

            case StudioVerificationDecision.Revoke:
                if (previousStatus != StudioVerificationStatus.Verified)
                {
                    return Result.Failure<ReviewStudioVerificationResult>(
                        Error.Conflict(
                            "StudioVerification.NotVerified",
                            "Only currently verified studios can have their verification revoked."));
                }

                targetStatus = StudioVerificationStatus.Rejected;
                studio.VerificationStatus = targetStatus;
                studio.VerifiedAt = null;
                studio.VerifiedBy = null;
                studio.VerificationNotes = trimmedFeedback;
                ClearPendingIdentityFields(studio);
                auditAction = "admin.studio_verification.revoke";
                beforeData = new { Status = "verified" };
                afterData = new { Status = "rejected", ReviewerFeedback = trimmedFeedback };
                break;

            default:
                return Result.Failure<ReviewStudioVerificationResult>(
                    Error.Validation("StudioVerification.InvalidDecision", "Unsupported verification decision."));
        }

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: auditAction,
            EntityType: "studio_profile",
            EntityId: studio.Id.ToString(),
            ActorType: AuditActorType.Admin,
            ActorUserId: command.AdminUserId,
            Reason: trimmedReason,
            BeforeData: beforeData,
            AfterData: afterData,
            RequireReason: true));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var owner = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == studio.OwnerUserId, cancellationToken);

        if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
        {
            await TrySendDecisionEmailAsync(
                owner,
                studio.Name,
                studio.Slug,
                command.Decision,
                trimmedFeedback,
                cancellationToken);
        }

        return Result.Success(new ReviewStudioVerificationResult(
            StudioId: studio.Id,
            StudioName: studio.Name,
            StudioSlug: studio.Slug,
            PreviousStatus: previousStatus,
            NewStatus: targetStatus,
            ReviewedAt: now));
    }

    private async Task<Result<StudioVerificationWorkspaceModel>> BuildWorkspaceModelAsync(
        Guid studioId,
        StudioMemberRole callerRole,
        CancellationToken cancellationToken)
    {
        var studio = await _dbContext.StudioProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == studioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioVerificationWorkspaceModel>(
                Error.NotFound("Studio.NotFound", "Studio workspace was not found."));
        }

        var owner = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == studio.OwnerUserId, cancellationToken);

        var ownerEmail = owner?.Email ?? string.Empty;
        var ownerDisplayName = !string.IsNullOrWhiteSpace(owner?.DisplayName)
            ? owner.DisplayName
            : ownerEmail;

        var engineCount = await _dbContext.StudioSkills
            .AsNoTracking()
            .CountAsync(
                ss => ss.StudioId == studio.Id &&
                      ss.Skill.IsActive &&
                      ss.Skill.Category == SkillCategory.Engine,
                cancellationToken);

        var titlesWithStoreUrls = await _dbContext.StudioTitles
            .AsNoTracking()
            .Where(t => t.StudioId == studio.Id && t.StoreUrl != null && t.StoreUrl != string.Empty)
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.ReleaseYear)
            .Select(t => new StudioTitleItem(
                t.Id,
                t.GameName,
                t.ReleaseYear,
                t.Platforms,
                t.StoreUrl,
                t.IgdbId,
                t.SortOrder))
            .ToListAsync(cancellationToken);

        var domainAssessment = StudioVerificationRules.AssessEmailDomain(ownerEmail, studio.Website);
        var readiness = StudioVerificationRules.EvaluateReadiness(
            studioName: studio.Name,
            description: studio.Description,
            cityId: studio.CityId,
            engineSkillCount: engineCount,
            normalizedWebsiteUrl: studio.Website,
            normalizedGstin: studio.Gstin,
            shippedTitleStoreUrlCount: titlesWithStoreUrls.Count,
            proofUrlCount: studio.VerificationProofUrls.Length);

        var pendingIdentity = BuildPendingIdentityChangeOrNull(studio);
        var isOwner = StudioProfileRules.CanSubmitVerification(callerRole);
        var canSubmitNow = isOwner &&
                           studio.VerificationStatus is StudioVerificationStatus.Unverified or StudioVerificationStatus.Rejected;
        var canRequestIdentityChange = isOwner &&
                                       studio.VerificationStatus == StudioVerificationStatus.Verified &&
                                       pendingIdentity is null;

        return Result.Success(new StudioVerificationWorkspaceModel(
            StudioId: studio.Id,
            StudioName: studio.Name,
            StudioSlug: studio.Slug,
            LogoUrl: ResolveLogoUrlOrNull(studio.LogoPath),
            CallerRole: callerRole,
            IsOwner: isOwner,
            CanSubmitNow: canSubmitNow,
            CanRequestIdentityChange: canRequestIdentityChange,
            OwnerDisplayName: ownerDisplayName,
            OwnerEmail: ownerEmail,
            Website: studio.Website,
            Gstin: studio.Gstin,
            DomainAssessment: domainAssessment,
            VerificationStatus: studio.VerificationStatus,
            VerificationSubmittedAt: studio.VerificationSubmittedAt,
            VerificationProofUrls: studio.VerificationProofUrls,
            VerificationApplicantNotes: studio.VerificationApplicantNotes,
            PendingIdentityChange: pendingIdentity,
            VerifiedAt: studio.VerifiedAt,
            ReviewerFeedback: studio.VerificationNotes,
            TitlesWithStoreUrls: titlesWithStoreUrls,
            Readiness: readiness));
    }

    private static StudioPendingIdentityChange? BuildPendingIdentityChangeOrNull(StudioProfile studio)
    {
        if (!studio.PendingIdentityRequestedAt.HasValue ||
            string.IsNullOrWhiteSpace(studio.PendingIdentityName) ||
            string.IsNullOrWhiteSpace(studio.PendingIdentitySlug))
        {
            return null;
        }

        return new StudioPendingIdentityChange(
            ProposedName: studio.PendingIdentityName,
            ProposedSlug: studio.PendingIdentitySlug,
            ProposedGstin: studio.PendingIdentityGstin,
            RequestedAt: studio.PendingIdentityRequestedAt.Value);
    }

    private static void ClearPendingIdentityFields(StudioProfile studio)
    {
        studio.PendingIdentityName = null;
        studio.PendingIdentitySlug = null;
        studio.PendingIdentityGstin = null;
        studio.PendingIdentityRequestedAt = null;
    }

    private Task<bool> IsSlugTakenOrReservedAsync(
        string normalizedSlug,
        Guid excludeStudioId,
        CancellationToken cancellationToken)
    {
        return _dbContext.StudioProfiles
            .AsNoTracking()
            .AnyAsync(
                s => s.Id != excludeStudioId &&
                     (s.Slug == normalizedSlug ||
                      (s.DeletedAt == null && s.PendingIdentitySlug == normalizedSlug)),
                cancellationToken);
    }

    private async Task<bool> IsActiveAdminUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var normalizedAdmin = HireGameDevRoleNames.Admin.ToUpperInvariant();
        return await (
            from u in _dbContext.Users.AsNoTracking()
            join ur in _dbContext.UserRoles.AsNoTracking() on u.Id equals ur.UserId
            join r in _dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where u.Id == userId &&
                  u.IsActive &&
                  u.DeletedAt == null &&
                  r.NormalizedName == normalizedAdmin
            select u.Id
        ).AnyAsync(cancellationToken);
    }

    private async Task TrySendSubmissionEmailAsync(
        ApplicationUser owner,
        string studioName,
        string studioSlug,
        bool isIdentityChange,
        CancellationToken cancellationToken)
    {
        try
        {
            var encodedOwner = WebUtility.HtmlEncode(owner.DisplayName);
            var encodedStudio = WebUtility.HtmlEncode(studioName);
            var encodedSlug = WebUtility.HtmlEncode(studioSlug);

            var subject = isIdentityChange
                ? $"Identity update request received for {studioName}"
                : $"Verification submitted for {studioName}";
            var tag = isIdentityChange
                ? "studio_identity_change_submitted"
                : "studio_verification_submitted";

            var htmlBody = isIdentityChange
                ? $"""
                    <h2>Identity re-verification request received</h2>
                    <p>Hi {encodedOwner},</p>
                    <p>We received your request to update the verified identity for <strong>{encodedStudio}</strong> (<code>/studios/{encodedSlug}</code>).</p>
                    <p>Your live verified studio page and badge remain active under your current details until our team completes review within 24–48 hours.</p>
                    """
                : $"""
                    <h2>Studio verification received</h2>
                    <p>Hi {encodedOwner},</p>
                    <p>We received the verification request for <strong>{encodedStudio}</strong> (<code>/studios/{encodedSlug}</code>).</p>
                    <p>Our team reviews studio submissions within 24–48 hours. You can continue refining your studio description, logo, titles, and draft roles in the meantime.</p>
                    """;

            var textBody = isIdentityChange
                ? $"Hi {owner.DisplayName},\n\nWe received your verified identity update request for '{studioName}' (/studios/{studioSlug}). Your live verified studio remains active under your current details during review (24-48 hours).\n"
                : $"Hi {owner.DisplayName},\n\nWe received the verification request for '{studioName}' (/studios/{studioSlug}). Our team reviews submissions within 24-48 hours.\n";

            await _emailSender.SendAsync(
                new EmailMessage(
                    To: owner.Email!,
                    Subject: subject,
                    HtmlBody: htmlBody,
                    TextBody: textBody,
                    Tag: tag),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send verification submission email for studio '{StudioSlug}'.", studioSlug);
        }
    }

    private async Task TrySendDecisionEmailAsync(
        ApplicationUser owner,
        string studioName,
        string studioSlug,
        StudioVerificationDecision decision,
        string? reviewerFeedback,
        CancellationToken cancellationToken)
    {
        try
        {
            var encodedOwner = WebUtility.HtmlEncode(owner.DisplayName);
            var encodedStudio = WebUtility.HtmlEncode(studioName);
            var encodedSlug = WebUtility.HtmlEncode(studioSlug);
            var encodedFeedback = !string.IsNullOrWhiteSpace(reviewerFeedback)
                ? WebUtility.HtmlEncode(reviewerFeedback)
                : null;

            string subject;
            string htmlBody;
            string textBody;
            string tag;

            switch (decision)
            {
                case StudioVerificationDecision.Approve:
                {
                    subject = $"{studioName} is now a Verified Studio on HireGameDev";
                    tag = "studio_verification_approved";
                    var noteBlock = encodedFeedback is not null
                        ? $"<p><strong>Note from reviewer:</strong> {encodedFeedback}</p>"
                        : string.Empty;

                    htmlBody = $"""
                        <h2>Your studio is verified!</h2>
                        <p>Hi {encodedOwner},</p>
                        <p><strong>{encodedStudio}</strong> is now officially verified on HireGameDev. Your public studio page at <code>/studios/{encodedSlug}</code> is now live to candidates, and job publishing is unlocked.</p>
                        {noteBlock}
                        """;

                    textBody =
                        $"Hi {owner.DisplayName},\n\n'{studioName}' is now officially verified on HireGameDev (/studios/{studioSlug})." +
                        (reviewerFeedback is not null ? $"\n\nReviewer note: {reviewerFeedback}\n" : "\n");
                    break;
                }

                case StudioVerificationDecision.ApproveIdentityChange:
                {
                    subject = $"Verified identity updated for {studioName}";
                    tag = "studio_identity_change_approved";
                    var noteBlock = encodedFeedback is not null
                        ? $"<p><strong>Note from reviewer:</strong> {encodedFeedback}</p>"
                        : string.Empty;

                    htmlBody = $"""
                        <h2>Your studio identity update is live!</h2>
                        <p>Hi {encodedOwner},</p>
                        <p>Your updated verified studio identity for <strong>{encodedStudio}</strong> (<code>/studios/{encodedSlug}</code>) has been approved and is now live.</p>
                        {noteBlock}
                        """;

                    textBody =
                        $"Hi {owner.DisplayName},\n\nYour updated verified identity for '{studioName}' (/studios/{studioSlug}) has been approved and is now live.\n";
                    break;
                }

                case StudioVerificationDecision.RejectIdentityChange:
                {
                    subject = $"Update needed: identity change request for {studioName}";
                    tag = "studio_identity_change_rejected";

                    htmlBody = $"""
                        <h2>Identity update request not approved</h2>
                        <p>Hi {encodedOwner},</p>
                        <p>Your live verified badge and public studio page for <strong>{encodedStudio}</strong> (<code>/studios/{encodedSlug}</code>) remain active and unchanged, but your proposed identity update needs attention:</p>
                        <blockquote style="margin:12px 0;padding:10px 14px;border-left:3px solid #f5c400;background:#f6f6f3;">{encodedFeedback}</blockquote>
                        <p>Open <strong>Studio Workspace &rarr; Verification</strong> when you are ready to submit an updated request.</p>
                        """;

                    textBody =
                        $"Hi {owner.DisplayName},\n\nYour live verified studio '{studioName}' (/studios/{studioSlug}) remains active, but your proposed identity update was not approved:\n\n{reviewerFeedback}\n";
                    break;
                }

                default:
                {
                    var actionLabel = decision == StudioVerificationDecision.Revoke
                        ? "verification was revoked"
                        : "needs an update before verification";

                    subject = $"Action needed: {studioName} studio verification";
                    tag = "studio_verification_changes_requested";

                    htmlBody = $"""
                        <h2>Update needed for studio verification</h2>
                        <p>Hi {encodedOwner},</p>
                        <p>Your studio workspace <strong>{encodedStudio}</strong> {actionLabel}:</p>
                        <blockquote style="margin:12px 0;padding:10px 14px;border-left:3px solid #f5c400;background:#f6f6f3;">{encodedFeedback}</blockquote>
                        <p>Open <strong>Studio Workspace &rarr; Verification</strong> to update your details and resubmit.</p>
                        """;

                    textBody =
                        $"Hi {owner.DisplayName},\n\n'{studioName}' {actionLabel}:\n\n{reviewerFeedback}\n\nOpen Studio Workspace -> Verification to update your details and resubmit.\n";
                    break;
                }
            }

            await _emailSender.SendAsync(
                new EmailMessage(
                    To: owner.Email!,
                    Subject: subject,
                    HtmlBody: htmlBody,
                    TextBody: textBody,
                    Tag: tag),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send verification decision email for studio '{StudioSlug}'.", studioSlug);
        }
    }

    private string? ResolveLogoUrlOrNull(string? logoObjectKey)
    {
        if (string.IsNullOrWhiteSpace(logoObjectKey))
        {
            return null;
        }

        return ObjectKeyRules.TryNormalize(logoObjectKey, out var normalized, out _)
            ? _fileStorage.ResolvePublicUrl(normalized)
            : null;
    }
}
