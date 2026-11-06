using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

/// <summary>
/// Shipped game credit on a candidate profile (Blueprint v2.0 Section 5, shipped_titles).
/// Verification status is strictly backend-controlled; NDA flags mask confidential project
/// and studio details on public/studio-facing projections while retaining internal records.
/// </summary>
public sealed class ShippedTitle
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public CandidateProfile? Candidate { get; set; }

    public string GameName { get; set; } = string.Empty;

    public string StudioName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Target platforms from canonical set: 'pc', 'android', 'ios', 'console', 'web'.
    /// </summary>
    public string[] Platforms { get; set; } = [];

    public int? EngineSkillId { get; set; }

    public Skill? EngineSkill { get; set; }

    public short? ReleaseYear { get; set; }

    public string? StoreUrl { get; set; }

    public long? IgdbId { get; set; }

    /// <summary>
    /// Backend-controlled verification tier ('none', 'title_verified', 'credit_verified').
    /// Never writable from candidate-submitted inputs.
    /// </summary>
    public ShippedTitleVerification Verification { get; set; } = ShippedTitleVerification.None;

    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>
    /// When true, hides GameName and StoreUrl on public/studio profiles (displaying "Confidential Project (Under NDA)").
    /// </summary>
    public bool IsNda { get; set; }

    /// <summary>
    /// When true alongside IsNda, also hides StudioName on public/studio profiles (displaying "Confidential Studio")
    /// while retaining internal StudioName for the candidate's private record.
    /// </summary>
    public bool HideStudioInPublic { get; set; }

    public short SortOrder { get; set; }
}
