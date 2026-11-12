using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Candidates;

public sealed record BlockedStudioItem(
    Guid StudioId,
    string Name,
    string Slug,
    string? LogoPath,
    string? CityName,
    string? StateCode,
    StudioType StudioType,
    DateTimeOffset BlockedAt);

public sealed record BlockableStudioSearchItem(
    Guid StudioId,
    string Name,
    string Slug,
    string? LogoPath,
    string? CityName,
    string? StateCode,
    StudioType StudioType,
    bool IsAlreadyBlocked);

public sealed record CandidateSettingsSnapshot(
    Guid CandidateId,
    Guid UserId,
    bool IsDiscoverable,
    CandidateAvailability Availability,
    decimal? CurrentCtcLpa,
    bool ShowCurrentCtc,
    decimal? ExpectedCtcLpa,
    short? NoticePeriodDays,
    bool OpenToRemote,
    bool WillingToRelocate,
    string? Phone,
    DateTimeOffset? PhoneVerifiedAt,
    short Completeness,
    bool MeetsApplyThreshold,
    bool MeetsDiscoverabilityThreshold,
    IReadOnlyList<BlockedStudioItem> BlockedStudios)
{
    public bool IsActivelyVisibleInStudioSearch =>
        IsDiscoverable && MeetsDiscoverabilityThreshold;
}

public sealed record UpdateCandidatePrivacySettingsCommand(
    Guid UserId,
    bool IsDiscoverable,
    CandidateAvailability Availability,
    decimal? CurrentCtcLpa,
    bool ShowCurrentCtc);

public sealed record BlockStudioCommand(
    Guid UserId,
    Guid StudioId);

public sealed record UnblockStudioCommand(
    Guid UserId,
    Guid StudioId);
