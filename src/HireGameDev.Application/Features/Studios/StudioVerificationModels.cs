using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Studios;

public sealed record StudioVerificationWorkspaceModel(
    Guid StudioId,
    string StudioName,
    string StudioSlug,
    string? LogoUrl,
    StudioMemberRole CallerRole,
    bool IsOwner,
    bool CanSubmitNow,
    bool CanRequestIdentityChange,
    string OwnerDisplayName,
    string OwnerEmail,
    string? Website,
    string? Gstin,
    StudioEmailDomainAssessment DomainAssessment,
    StudioVerificationStatus VerificationStatus,
    DateTimeOffset? VerificationSubmittedAt,
    IReadOnlyList<string> VerificationProofUrls,
    string? VerificationApplicantNotes,
    StudioPendingIdentityChange? PendingIdentityChange,
    DateTimeOffset? VerifiedAt,
    string? ReviewerFeedback,
    IReadOnlyList<StudioTitleItem> TitlesWithStoreUrls,
    StudioVerificationReadiness Readiness);

public sealed record SubmitStudioVerificationCommand(
    Guid ActorUserId,
    Guid StudioId,
    string? Website,
    string? Gstin,
    IReadOnlyList<string> ProofUrls,
    string? ApplicantNotes);

public sealed record RequestStudioIdentityChangeCommand(
    Guid ActorUserId,
    Guid StudioId,
    string ProposedName,
    string ProposedSlug,
    string? ProposedGstin,
    IReadOnlyList<string> ProofUrls,
    string ApplicantNotes);

public sealed record CancelStudioIdentityChangeCommand(
    Guid ActorUserId,
    Guid StudioId);

public sealed record AdminStudioVerificationQueueCounts(
    int PendingCount,
    int PendingInitialCount,
    int PendingIdentityChangeCount,
    int VerifiedCount,
    int RejectedCount,
    int UnverifiedCount,
    int TotalCount);

public sealed record AdminStudioVerificationQueueItem(
    Guid StudioId,
    string StudioName,
    string StudioSlug,
    string? LogoUrl,
    StudioType StudioType,
    StudioSizeBand SizeBand,
    string? CityName,
    string? CityState,
    Guid OwnerUserId,
    string OwnerDisplayName,
    string OwnerEmail,
    StudioEmailDomainAssessment DomainAssessment,
    string? Website,
    string? Gstin,
    IReadOnlyList<string> ProofUrls,
    string? ApplicantNotes,
    bool HasPendingIdentityChange,
    StudioPendingIdentityChange? PendingIdentityChange,
    IReadOnlyList<StudioTitleItem> Titles,
    IReadOnlyList<string> EngineNames,
    StudioVerificationStatus VerificationStatus,
    DateTimeOffset? VerificationSubmittedAt,
    double? WaitingHours,
    bool IsPastTargetSla,
    DateTimeOffset? VerifiedAt,
    string? VerifiedByEmail,
    string? ReviewerFeedback,
    DateTimeOffset UpdatedAt);

public sealed record AdminStudioVerificationQueueModel(
    StudioVerificationStatus? ActiveFilter,
    AdminStudioVerificationQueueCounts Counts,
    IReadOnlyList<AdminStudioVerificationQueueItem> Items);

public sealed record ReviewStudioVerificationCommand(
    Guid AdminUserId,
    Guid StudioId,
    StudioVerificationDecision Decision,
    string Reason,
    string? ReviewerFeedback);

public sealed record ReviewStudioVerificationResult(
    Guid StudioId,
    string StudioName,
    string StudioSlug,
    StudioVerificationStatus PreviousStatus,
    StudioVerificationStatus NewStatus,
    DateTimeOffset ReviewedAt);
