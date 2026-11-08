namespace HireGameDev.Domain.Enums;

/// <summary>
/// Canonical user-facing display labels for <see cref="SkillEvidenceLevel"/> across Candidate and Studio views.
/// </summary>
public static class SkillEvidenceLevelExtensions
{
    public const string SelfDeclaredLabel = "Self-declared";
    public const string SupportingEvidenceSubmittedLabel = "Supporting evidence submitted";
    public const string IndependentlyVerifiedLabel = "Independently verified";

    public static string ToDisplayLabel(this SkillEvidenceLevel level) => level switch
    {
        SkillEvidenceLevel.Verified => IndependentlyVerifiedLabel,
        SkillEvidenceLevel.Backed => SupportingEvidenceSubmittedLabel,
        _ => SelfDeclaredLabel
    };
}
