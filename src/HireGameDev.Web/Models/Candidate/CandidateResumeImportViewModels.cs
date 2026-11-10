using HireGameDev.Application.Features.Candidates;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Web.Models.Candidate;

public sealed class ResumeImportPageViewModel
{
    public bool HasExistingProfile { get; init; }

    public bool HasParsedDraft { get; init; }

    public ResumeParseQuotaStatus Quota { get; init; } =
        new(ResumePdfUploadRules.DefaultDailyParseLimit, 0, ResumePdfUploadRules.DefaultDailyParseLimit);

    public ResumeParseSource? DetectedSource { get; init; }

    public string? ProviderName { get; init; }

    public IReadOnlyList<CityLookupItem> Cities { get; init; } = [];

    public IReadOnlyList<SkillLookupItem> EngineSkills { get; init; } = [];

    public ResumeDraftConfirmInputModel Review { get; init; } = new();
}

public sealed class ResumeDraftConfirmInputModel
{
    // Populated/required only when CandidateProfile has not been created via Step 1 Basics yet.
    public string FullName { get; set; } = string.Empty;

    public DisciplineCategory PrimaryCategory { get; set; } = DisciplineCategory.Programming;

    public int CityId { get; set; }

    public bool ApplyHeadline { get; set; }

    public string? Headline { get; set; }

    public bool ApplyYearsExperience { get; set; }

    public decimal? YearsExperience { get; set; }

    public List<ResumeDraftSkillRowInputModel> Skills { get; set; } = [];

    public List<string> UnmatchedSkillSuggestions { get; set; } = [];

    public List<ResumeDraftExperienceRowInputModel> Experiences { get; set; } = [];

    public List<ResumeDraftEducationRowInputModel> Education { get; set; } = [];

    public List<ResumeDraftShippedTitleRowInputModel> ShippedTitles { get; set; } = [];
}

public sealed class ResumeDraftSkillRowInputModel
{
    public bool Include { get; set; }

    public int SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public SkillCategory Category { get; set; }

    public SkillTaxonomyMatchKind MatchKind { get; set; } = SkillTaxonomyMatchKind.ExactNameOrAlias;

    public string RawExtractedName { get; set; } = string.Empty;

    public SkillProficiency Proficiency { get; set; } = SkillProficiency.Working;

    public decimal? YearsUsed { get; set; }

    public short? LastUsedYear { get; set; }

    public bool IsPrimary { get; set; }
}

public sealed class ResumeDraftExperienceRowInputModel
{
    public bool Include { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool IsCurrent { get; set; }

    public string? Description { get; set; }
}

public sealed class ResumeDraftEducationRowInputModel
{
    public bool Include { get; set; }

    public string Institution { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public string? Field { get; set; }

    public short? StartYear { get; set; }

    public short? EndYear { get; set; }
}

public sealed class ResumeDraftShippedTitleRowInputModel
{
    public bool Include { get; set; }

    public string GameName { get; set; } = string.Empty;

    public string StudioName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public short? ReleaseYear { get; set; }

    public int? EngineSkillId { get; set; }

    public List<string> Platforms { get; set; } = [];
}
