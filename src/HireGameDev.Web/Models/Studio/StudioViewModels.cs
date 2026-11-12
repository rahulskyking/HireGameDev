using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Web.Models.Studio;

public sealed class StudioTitleRowInputModel
{
    public Guid? Id { get; set; }

    public string GameName { get; set; } = string.Empty;

    public short? ReleaseYear { get; set; }

    public List<string> Platforms { get; set; } = [];

    public string? StoreUrl { get; set; }
}

public sealed class StudioProfileFormInputModel
{
    public Guid? StudioId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public string? Website { get; set; }

    public string Description { get; set; } = string.Empty;

    public int CityId { get; set; }

    public StudioSizeBand SizeBand { get; set; } = StudioSizeBand.Band1To10;

    public StudioType StudioType { get; set; } = StudioType.Indie;

    public short? FoundedYear { get; set; }

    public List<string> CultureTags { get; set; } = [];

    public string? Gstin { get; set; }

    public List<int> SkillIds { get; set; } = [];

    public List<StudioTitleRowInputModel> Titles { get; set; } = [];
}

public sealed class StudioWorkspacePageViewModel
{
    public bool IsOnboarding { get; init; }

    public StudioProfileDetail? Studio { get; init; }

    public StudioMembershipSummary? OwnedActiveStudio { get; init; }

    public IReadOnlyList<StudioMembershipSummary> Memberships { get; init; } = [];

    public StudioProfileFormInputModel Form { get; init; } = new();

    public IReadOnlyList<CityLookupItem> Cities { get; init; } = [];

    public IReadOnlyList<SkillLookupItem> EngineSkills { get; init; } = [];

    public IReadOnlyList<SkillLookupItem> GenreSkills { get; init; } = [];

    public IReadOnlyList<SkillLookupItem> PlatformSkills { get; init; } = [];
}

public sealed class AddStudioTeamMemberInputModel
{
    public string Email { get; set; } = string.Empty;

    public StudioMemberRole MemberRole { get; set; } = StudioMemberRole.Recruiter;
}

public sealed class StudioTeamPageViewModel
{
    public required StudioTeamState Team { get; init; }

    public StudioMembershipSummary? OwnedActiveStudio { get; init; }

    public IReadOnlyList<StudioMembershipSummary> Memberships { get; init; } = [];

    public AddStudioTeamMemberInputModel AddMember { get; init; } = new();
}

public sealed record StudioWorkspaceNavViewModel(
    Guid? ActiveStudioId,
    string ActiveTab,
    StudioMembershipSummary? OwnedActiveStudio,
    IReadOnlyList<StudioMembershipSummary> Memberships);

public sealed class PublicStudioPageViewModel
{
    public required PublicStudioPageModel Studio { get; init; }

    public string CanonicalPublicUrl { get; init; } = string.Empty;
}

public sealed class SubmitStudioVerificationInputModel
{
    public Guid StudioId { get; set; }

    public string? Website { get; set; }

    public string? Gstin { get; set; }

    public List<string> ProofUrls { get; set; } = [];

    public string? ApplicantNotes { get; set; }
}

public sealed class RequestStudioIdentityChangeInputModel
{
    public Guid StudioId { get; set; }

    public string ProposedName { get; set; } = string.Empty;

    public string ProposedSlug { get; set; } = string.Empty;

    public string? ProposedGstin { get; set; }

    public List<string> ProofUrls { get; set; } = [];

    public string ApplicantNotes { get; set; } = string.Empty;
}

public sealed class StudioVerificationPageViewModel
{
    public required StudioVerificationWorkspaceModel Workspace { get; init; }

    public StudioMembershipSummary? OwnedActiveStudio { get; init; }

    public IReadOnlyList<StudioMembershipSummary> Memberships { get; init; } = [];

    public SubmitStudioVerificationInputModel SubmitForm { get; init; } = new();

    public RequestStudioIdentityChangeInputModel IdentityChangeForm { get; init; } = new();

    public bool ExpandIdentityChangeForm { get; init; }
}

public static class StudioUiFormatters
{
    public static readonly IReadOnlyList<(string Slug, string Label)> PlatformOptions =
    [
        ("pc", "PC"),
        ("android", "Android"),
        ("ios", "iOS"),
        ("console", "Console"),
        ("web", "Web")
    ];

    public static string FormatPlatformLabel(string? slug) =>
        slug?.Trim().ToLowerInvariant() switch
        {
            "pc" => "PC",
            "android" => "Android",
            "ios" => "iOS",
            "console" => "Console",
            "web" => "Web",
            _ => string.IsNullOrWhiteSpace(slug) ? "Other" : slug.Trim()
        };

    public static string BuildMonogram(string? studioName)
    {
        if (string.IsNullOrWhiteSpace(studioName))
        {
            return "ST";
        }

        var parts = studioName
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 2)
        {
            return string.Concat(
                char.ToUpperInvariant(parts[0][0]),
                char.ToUpperInvariant(parts[1][0]));
        }

        var first = parts[0];
        return first.Length >= 2
            ? string.Concat(char.ToUpperInvariant(first[0]), char.ToUpperInvariant(first[1]))
            : char.ToUpperInvariant(first[0]).ToString();
    }
}
