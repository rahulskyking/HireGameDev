using System.ComponentModel.DataAnnotations;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Web.Models.Candidate;

public sealed class CandidateOnboardingWizardViewModel
{
    public short ActiveStep { get; init; } = 1;

    public bool HasExistingProfile { get; init; }

    public shortHighestStepInfo HighestReachedStep => new(OnboardingState.Profile?.OnboardingStep ?? 1);

    public CandidateOnboardingState OnboardingState { get; init; } = default!;

    public IReadOnlyList<CityLookupItem> Cities { get; init; } = [];

    public IReadOnlyList<SkillLookupItem> AvailableSkills { get; init; } = [];

    public Step1BasicsInputModel Step1 { get; init; } = new();

    public Step2ExperienceCoreInputModel Step2 { get; init; } = new();

    public Step3SkillsInputModel Step3 { get; init; } = new();

    public Step5PreferencesInputModel Step5 { get; init; } = new();

    public readonly record struct shortHighestStepInfo(short Value);
}

public sealed class Step1BasicsInputModel
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 80 characters.")]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Headline is required.")]
    [StringLength(80, MinimumLength = 3, ErrorMessage = "Headline must be between 3 and 80 characters.")]
    [Display(Name = "Role headline")]
    public string Headline { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select your primary discipline.")]
    [Display(Name = "Primary discipline")]
    public DisciplineCategory PrimaryCategory { get; set; } = DisciplineCategory.Programming;

    [Range(1, int.MaxValue, ErrorMessage = "Select your primary city from the list.")]
    [Display(Name = "Primary city")]
    public int CityId { get; set; }

    [StringLength(1500, ErrorMessage = "Bio must be 1500 characters or fewer.")]
    [Display(Name = "Developer bio (optional)")]
    public string? Bio { get; set; }
}

public sealed class Step2ExperienceCoreInputModel
{
    [Range(0.0, 50.0, ErrorMessage = "Years of experience must be between 0 and 50.")]
    [Display(Name = "Total game/software experience (years)")]
    public decimal YearsExperience { get; set; }

    [Required(ErrorMessage = "Select your seniority level.")]
    [Display(Name = "Seniority level")]
    public SeniorityLevel Seniority { get; set; } = SeniorityLevel.Fresher;

    public List<CandidateExperienceInputRow> Experiences { get; set; } = [];

    public List<CandidateEducationInputRow> Education { get; set; } = [];
}

public sealed class CandidateExperienceInputRow
{
    public Guid? Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool IsCurrent { get; set; }

    public string? Description { get; set; }

    public bool IsCompletelyEmpty =>
        string.IsNullOrWhiteSpace(CompanyName) &&
        string.IsNullOrWhiteSpace(Title) &&
        !StartDate.HasValue &&
        !EndDate.HasValue &&
        string.IsNullOrWhiteSpace(Description);
}

public sealed class CandidateEducationInputRow
{
    public Guid? Id { get; set; }

    public string Institution { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public string? Field { get; set; }

    public short? StartYear { get; set; }

    public short? EndYear { get; set; }

    public bool IsCompletelyEmpty =>
        string.IsNullOrWhiteSpace(Institution) &&
        string.IsNullOrWhiteSpace(Degree) &&
        string.IsNullOrWhiteSpace(Field) &&
        !StartYear.HasValue &&
        !EndYear.HasValue;
}

public sealed class Step3SkillsInputModel
{
    public List<CandidateSkillInputRow> Skills { get; set; } = [];
}

public sealed class CandidateSkillInputRow
{
    public int SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public string CategoryLabel { get; set; } = string.Empty;

    public SkillProficiency Proficiency { get; set; } = SkillProficiency.Working;

    public decimal? YearsUsed { get; set; }

    public short? LastUsedYear { get; set; }

    public bool IsPrimary { get; set; }

    public SkillEvidenceLevel EvidenceLevel { get; set; } = SkillEvidenceLevel.Claimed;
}

public sealed class Step5PreferencesInputModel
{
    [Required(ErrorMessage = "Expected CTC (₹ LPA) is required.")]
    [Range(0.5, 500.0, ErrorMessage = "Expected CTC must be between 0.5 and 500 ₹ LPA.")]
    [Display(Name = "Expected CTC (₹ LPA)")]
    public decimal? ExpectedCtcLpa { get; set; }

    [Range(0.0, 500.0, ErrorMessage = "Current CTC must be between 0 and 500 ₹ LPA.")]
    [Display(Name = "Current CTC (₹ LPA, optional)")]
    public decimal? CurrentCtcLpa { get; set; }

    [Display(Name = "Allow verified studios to see my current CTC")]
    public bool ShowCurrentCtc { get; set; }

    [Required(ErrorMessage = "Select your notice period.")]
    [Display(Name = "Notice period")]
    public short? NoticePeriodDays { get; set; }

    [Display(Name = "Open to remote roles within India")]
    public bool OpenToRemote { get; set; }

    [Display(Name = "Willing to relocate to another Indian gaming hub")]
    public bool WillingToRelocate { get; set; }

    [Required(ErrorMessage = "Select your job-search availability.")]
    [Display(Name = "Current availability")]
    public CandidateAvailability Availability { get; set; } = CandidateAvailability.ActivelyLooking;
}

public sealed class CandidateProfileOverviewViewModel
{
    public CandidateProfileSnapshot Profile { get; init; } = default!;

    public HireGameDev.Domain.Rules.CandidateCompletenessBreakdown Completeness { get; init; } = default!;

    public IReadOnlyList<CandidateSkillItem> Skills { get; init; } = [];

    public IReadOnlyList<CandidateExperienceItem> Experiences { get; init; } = [];

    public IReadOnlyList<CandidateEducationItem> Education { get; init; } = [];
}
