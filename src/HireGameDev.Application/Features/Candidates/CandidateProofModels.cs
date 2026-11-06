using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Candidates;

public sealed record PortfolioEvidencedSkillItem(
    int SkillId,
    string SkillName);

/// <summary>
/// Private owner-only view of a shipped game credit (retains internal GameName, StudioName, and StoreUrl under NDA).
/// Verification is read-only and strictly backend-controlled.
/// </summary>
public sealed record CandidateShippedTitleOwnerItem(
    Guid Id,
    string GameName,
    string StudioName,
    string Role,
    IReadOnlyList<string> Platforms,
    int? EngineSkillId,
    string? EngineSkillName,
    short? ReleaseYear,
    string? StoreUrl,
    ShippedTitleVerification Verification,
    DateTimeOffset? VerifiedAt,
    bool IsNda,
    bool HideStudioInPublic,
    short SortOrder)
{
    public bool IsBackendVerified => Verification != ShippedTitleVerification.None;
}

/// <summary>
/// Public/studio-safe projection of a shipped game credit.
/// Confidential project names and store URLs are masked whenever IsNda is true,
/// and confidential studio names are masked whenever IsNda and HideStudioInPublic are true.
/// </summary>
public sealed record PublicShippedTitleItem(
    Guid Id,
    string DisplayTitle,
    string DisplayStudio,
    string Role,
    IReadOnlyList<string> Platforms,
    int? EngineSkillId,
    string? EngineSkillName,
    short? ReleaseYear,
    string? PublicStoreUrl,
    ShippedTitleVerification Verification,
    DateTimeOffset? VerifiedAt,
    bool IsNda,
    bool IsStudioRedacted,
    short SortOrder)
{
    public const string NdaProjectLabel = "Confidential Project (Under NDA)";
    public const string NdaStudioLabel = "Confidential Studio";

    public bool IsBackendVerified => Verification != ShippedTitleVerification.None;
}

/// <summary>
/// Candidate-submitted portfolio link item. Represents submitted proof (never verified proof on its own).
/// </summary>
public sealed record CandidatePortfolioLinkItem(
    Guid Id,
    PortfolioLinkType LinkType,
    string Url,
    string? Title,
    IReadOnlyList<PortfolioEvidencedSkillItem> EvidencedSkills,
    short SortOrder);

public sealed record CandidateOwnerProofSnapshot(
    IReadOnlyList<CandidateShippedTitleOwnerItem> ShippedTitles,
    IReadOnlyList<CandidatePortfolioLinkItem> PortfolioLinks);

public sealed record CandidatePublicProofSnapshot(
    IReadOnlyList<PublicShippedTitleItem> ShippedTitles,
    IReadOnlyList<CandidatePortfolioLinkItem> PortfolioLinks);

/// <summary>
/// Candidate-submitted shipped title input. Intentionally omits Verification and VerifiedAt
/// so candidates can never self-verify game credits.
/// </summary>
public sealed record SaveShippedTitleInput(
    Guid? Id,
    string GameName,
    string StudioName,
    string Role,
    IReadOnlyList<string> Platforms,
    int? EngineSkillId,
    short? ReleaseYear,
    string? StoreUrl,
    bool IsNda,
    bool HideStudioInPublic);

public sealed record SavePortfolioLinkInput(
    Guid? Id,
    PortfolioLinkType LinkType,
    string Url,
    string? Title,
    IReadOnlyList<int> SkillIds);

public sealed record SaveCandidateStep4ProofCommand(
    Guid UserId,
    IReadOnlyList<SaveShippedTitleInput> ShippedTitles,
    IReadOnlyList<SavePortfolioLinkInput> PortfolioLinks);
