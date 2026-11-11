using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Studios;

public sealed record StudioMembershipSummary(
    Guid StudioId,
    string Name,
    string Slug,
    string? LogoUrl,
    StudioMemberRole MemberRole,
    StudioVerificationStatus VerificationStatus,
    string? CityName);

public sealed record StudioSlugAvailabilityResult(
    string NormalizedSlug,
    bool IsAvailable,
    string? Reason);

public sealed record StudioSkillItem(
    int SkillId,
    string Name,
    string Slug,
    SkillCategory Category);

public sealed record StudioTitleItem(
    Guid Id,
    string GameName,
    short? ReleaseYear,
    IReadOnlyList<string> Platforms,
    string? StoreUrl,
    long? IgdbId,
    short SortOrder);

public sealed record StudioTitleInput(
    Guid? Id,
    string GameName,
    short? ReleaseYear,
    IReadOnlyList<string> Platforms,
    string? StoreUrl);

public sealed record StudioProfileDetail(
    Guid Id,
    Guid OwnerUserId,
    StudioMemberRole CallerRole,
    bool CanEditProfile,
    bool CanEditIdentityFields,
    bool CanEditSlug,
    bool CanManageTeam,
    bool CanSubmitVerification,
    string Name,
    string Slug,
    string? LogoObjectKey,
    string? LogoUrl,
    string? Website,
    string? CompanyEmailDomain,
    string? Description,
    int? CityId,
    string? CityName,
    string? CityState,
    StudioSizeBand SizeBand,
    StudioType StudioType,
    short? FoundedYear,
    IReadOnlyList<string> CultureTags,
    string? Gstin,
    StudioVerificationStatus VerificationStatus,
    DateTimeOffset? VerificationSubmittedAt,
    IReadOnlyList<string> VerificationProofUrls,
    string? VerificationApplicantNotes,
    DateTimeOffset? VerifiedAt,
    string? VerificationNotes,
    int Apps90d,
    decimal? ResponseRate90d,
    decimal? MedianFirstResponseHours,
    IReadOnlyList<StudioSkillItem> Skills,
    IReadOnlyList<StudioTitleItem> Titles,
    int MemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateStudioProfileCommand(
    Guid ActorUserId,
    string Name,
    string? Slug,
    string? Website,
    string Description,
    int CityId,
    StudioSizeBand SizeBand,
    StudioType StudioType,
    short? FoundedYear,
    IReadOnlyList<string> CultureTags,
    string? Gstin,
    IReadOnlyList<int> SkillIds,
    IReadOnlyList<StudioTitleInput> Titles);

public sealed record UpdateStudioProfileCommand(
    Guid ActorUserId,
    Guid StudioId,
    string Name,
    string? Slug,
    string? Website,
    string Description,
    int CityId,
    StudioSizeBand SizeBand,
    StudioType StudioType,
    short? FoundedYear,
    IReadOnlyList<string> CultureTags,
    string? Gstin,
    IReadOnlyList<int> SkillIds,
    IReadOnlyList<StudioTitleInput> Titles);

public sealed record UploadStudioLogoCommand(
    Guid ActorUserId,
    Guid StudioId,
    Stream Content,
    string? FileName,
    string? DeclaredContentType);

public sealed record StudioLogoMutationResult(
    Guid StudioId,
    string? LogoObjectKey,
    string? LogoUrl,
    DateTimeOffset UpdatedAt);

public sealed record StudioTeamMemberItem(
    Guid UserId,
    string DisplayName,
    string Email,
    StudioMemberRole MemberRole,
    DateTimeOffset InvitedAt,
    DateTimeOffset? AcceptedAt,
    bool IsOwner);

public sealed record StudioTeamState(
    Guid StudioId,
    string StudioName,
    string StudioSlug,
    StudioMemberRole CallerRole,
    bool CanManageTeam,
    IReadOnlyList<StudioTeamMemberItem> Members);

public sealed record AddStudioMemberCommand(
    Guid ActorUserId,
    Guid StudioId,
    string Email,
    StudioMemberRole MemberRole);

public sealed record UpdateStudioMemberRoleCommand(
    Guid ActorUserId,
    Guid StudioId,
    Guid TargetUserId,
    StudioMemberRole NewRole);

public sealed record RemoveStudioMemberCommand(
    Guid ActorUserId,
    Guid StudioId,
    Guid TargetUserId);

public sealed record PublicStudioViewer(
    bool IsAuthenticated,
    Guid? UserId,
    bool IsAdmin);

public sealed record PublicStudioPageModel(
    Guid StudioId,
    string Name,
    string Slug,
    string? LogoUrl,
    string? Website,
    string? Description,
    string? CityName,
    string? CityState,
    StudioSizeBand SizeBand,
    StudioType StudioType,
    short? FoundedYear,
    IReadOnlyList<string> CultureTags,
    StudioVerificationStatus VerificationStatus,
    bool IsVerified,
    bool IsPreviewMode,
    StudioMemberRole? ViewerMemberRole,
    int Apps90d,
    decimal? ResponseRate90d,
    decimal? MedianFirstResponseHours,
    IReadOnlyList<StudioSkillItem> Engines,
    IReadOnlyList<StudioSkillItem> Genres,
    IReadOnlyList<StudioSkillItem> Platforms,
    IReadOnlyList<StudioTitleItem> Titles);
