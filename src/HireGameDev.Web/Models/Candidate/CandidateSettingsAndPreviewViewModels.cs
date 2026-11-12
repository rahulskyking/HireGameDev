using System.ComponentModel.DataAnnotations;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Web.Models.Candidate;

public sealed record CandidateWorkspaceNavViewModel(
    string ActiveTab,
    short CompletenessScore,
    int BlockedStudiosCount = 0);

public sealed record CompletenessChecklistViewModel(
    CandidateCompletenessBreakdown Completeness,
    bool IsCompact = false);

/// <summary>
/// Studio-facing candidate profile preview view model (Blueprint Page 33, ADR-020).
/// Deliberately excludes <see cref="CandidateOwnerProofSnapshot"/> and raw phone/email values
/// so WYSIWYG studio preview never leaks private NDA records or gated contact details.
/// </summary>
public sealed class CandidateProfilePreviewViewModel
{
    public required CandidateProfileSnapshot Profile { get; init; }

    public required CandidateCompletenessBreakdown Completeness { get; init; }

    public IReadOnlyList<CandidateSkillItem> Skills { get; init; } = Array.Empty<CandidateSkillItem>();

    public IReadOnlyList<CandidateExperienceItem> Experiences { get; init; } = Array.Empty<CandidateExperienceItem>();

    public IReadOnlyList<CandidateEducationItem> Education { get; init; } = Array.Empty<CandidateEducationItem>();

    public CandidatePublicProofSnapshot PublicProof { get; init; } = new(
        Array.Empty<PublicShippedTitleItem>(),
        Array.Empty<PortfolioLinkItem>());

    public int BlockedStudiosCount { get; init; }
}

public sealed class UpdateCandidatePrivacyInputModel
{
    [Display(Name = "Allow verified studios to discover my profile in talent search")]
    public bool IsDiscoverable { get; set; } = true;

    [Required(ErrorMessage = "Select your current availability.")]
    [Display(Name = "Job-search availability")]
    public CandidateAvailability Availability { get; set; } = CandidateAvailability.ActivelyLooking;

    [Range(0.0, 500.0, ErrorMessage = "Current CTC must be between 0 and 500 ₹ LPA.")]
    [Display(Name = "Current CTC (₹ LPA, optional)")]
    public decimal? CurrentCtcLpa { get; set; }

    [Display(Name = "Show my Current CTC to verified studios")]
    public bool ShowCurrentCtc { get; set; }
}

public sealed record BlockedStudioRowViewModel(
    Guid StudioId,
    string Name,
    string Slug,
    string? LogoUrl,
    string PublicStudioUrl,
    string? CityName,
    string? StateCode,
    StudioType StudioType,
    DateTimeOffset BlockedAt);

public sealed record BlockableStudioSearchRowViewModel(
    Guid StudioId,
    string Name,
    string Slug,
    string? LogoUrl,
    string PublicStudioUrl,
    string? CityName,
    string? StateCode,
    StudioType StudioType,
    bool IsAlreadyBlocked);

public sealed class CandidateSettingsPageViewModel
{
    public required CandidateSettingsSnapshot Settings { get; init; }

    public required CandidateCompletenessBreakdown Completeness { get; init; }

    public UpdateCandidatePrivacyInputModel PrivacyForm { get; init; } = new();

    public IReadOnlyList<BlockedStudioRowViewModel> BlockedStudios { get; init; } =
        Array.Empty<BlockedStudioRowViewModel>();

    public string? StudioSearchQuery { get; init; }

    public IReadOnlyList<BlockableStudioSearchRowViewModel> SearchResults { get; init; } =
        Array.Empty<BlockableStudioSearchRowViewModel>();
}
