using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

public sealed record ResumeParseQuotaStatus(
    int DailyLimit,
    int UsedToday,
    int RemainingToday)
{
    public bool HasQuotaRemaining => RemainingToday > 0;
}

public sealed record ExtractedResumePdfDocument(
    string ExtractedText,
    int NonWhitespaceCharCount,
    int PageCount,
    ResumeParseSource DetectedSource);

public sealed record SanitizedResumeSkillExtraction(
    string Name,
    decimal? YearsUsed,
    short? LastUsedYear);

public sealed record SanitizedResumeExperienceExtraction(
    string CompanyName,
    string Title,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description);

public sealed record SanitizedResumeEducationExtraction(
    string Institution,
    string Degree,
    string? Field,
    short? StartYear,
    short? EndYear);

public sealed record SanitizedResumeShippedTitleExtraction(
    string GameName,
    string? StudioName,
    string? Role,
    short? ReleaseYear);

public sealed record SanitizedResumeExtractionPayload(
    string? Headline,
    decimal? YearsExperience,
    IReadOnlyList<SanitizedResumeSkillExtraction> Skills,
    IReadOnlyList<SanitizedResumeExperienceExtraction> Experiences,
    IReadOnlyList<SanitizedResumeEducationExtraction> Education,
    IReadOnlyList<SanitizedResumeShippedTitleExtraction> ShippedTitles);

public sealed record ResumeDraftMatchedSkill(
    int SkillId,
    string SkillName,
    string SkillSlug,
    SkillCategory Category,
    SkillTaxonomyMatchKind MatchKind,
    SkillProficiency SuggestedProficiency,
    decimal? YearsUsed,
    short? LastUsedYear,
    string RawExtractedName);

public sealed record ResumeImportDraft(
    ResumeParseSource Source,
    string ProviderName,
    string? Headline,
    decimal? YearsExperience,
    IReadOnlyList<ResumeDraftMatchedSkill> MatchedSkills,
    IReadOnlyList<string> UnmatchedSkillSuggestions,
    IReadOnlyList<SanitizedResumeExperienceExtraction> Experiences,
    IReadOnlyList<SanitizedResumeEducationExtraction> Education,
    IReadOnlyList<SanitizedResumeShippedTitleExtraction> ShippedTitles,
    ResumeParseQuotaStatus QuotaAfterParse);

public sealed record ParseCandidateResumeCommand(
    Guid UserId,
    Stream PdfStream,
    string? FileName);

public sealed record ConfirmImportedSkillInput(
    int SkillId,
    SkillProficiency Proficiency,
    decimal? YearsUsed,
    short? LastUsedYear,
    bool IsPrimary);

public sealed record ConfirmImportedExperienceInput(
    string CompanyName,
    string Title,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description);

public sealed record ConfirmImportedEducationInput(
    string Institution,
    string Degree,
    string? Field,
    short? StartYear,
    short? EndYear);

public sealed record ConfirmImportedShippedTitleInput(
    string GameName,
    string StudioName,
    string Role,
    IReadOnlyList<string> Platforms,
    int? EngineSkillId,
    short? ReleaseYear);

public sealed record ApplyCandidateResumeDraftCommand(
    Guid UserId,
    bool ApplyHeadline,
    string? Headline,
    bool ApplyYearsExperience,
    decimal? YearsExperience,
    IReadOnlyList<ConfirmImportedSkillInput> Skills,
    IReadOnlyList<ConfirmImportedExperienceInput> Experiences,
    IReadOnlyList<ConfirmImportedEducationInput> Education,
    IReadOnlyList<ConfirmImportedShippedTitleInput> ShippedTitles);

public sealed record ApplyCandidateResumeResult(
    int AddedSkillCount,
    int UpdatedSkillCount,
    int AddedExperienceCount,
    int AddedEducationCount,
    int AddedShippedTitleCount,
    short UpdatedCompleteness);
