using HireGameDev.Domain.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class CandidateProfile : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Headline { get; set; } = string.Empty;

    public DisciplineCategory PrimaryCategory { get; set; } = DisciplineCategory.Programming;

    public string? PhotoPath { get; set; }

    /// <summary>
    /// E.164 formatted phone number (e.g. +919876543210). Hidden from studios until shortlist or accepted invite.
    /// </summary>
    public string? Phone { get; set; }

    public DateTimeOffset? PhoneVerifiedAt { get; set; }

    public int CityId { get; set; }

    public City? City { get; set; }

    public SeniorityLevel Seniority { get; set; } = SeniorityLevel.Fresher;

    public decimal YearsExperience { get; set; }

    /// <summary>
    /// Optional current compensation in INR Lakhs Per Annum. Never shown to studios unless ShowCurrentCtc is true.
    /// </summary>
    public decimal? CurrentCtcLpa { get; set; }

    public bool ShowCurrentCtc { get; set; }

    /// <summary>
    /// Expected compensation in INR Lakhs Per Annum (> 0). Nullable on Step 1-4 onboarding drafts; required in Step 5.
    /// </summary>
    public decimal? ExpectedCtcLpa { get; set; }

    /// <summary>
    /// Notice period in days (0, 15, 30, 45, 60, 90). Nullable on Step 1-4 onboarding drafts; required in Step 5.
    /// </summary>
    public short? NoticePeriodDays { get; set; }

    public bool OpenToRemote { get; set; }

    public bool WillingToRelocate { get; set; }

    public CandidateAvailability Availability { get; set; } = CandidateAvailability.ActivelyLooking;

    public string? Bio { get; set; }

    public short Completeness { get; set; }

    /// <summary>
    /// Tracks highest onboarding wizard step reached (1..5).
    /// </summary>
    public short OnboardingStep { get; set; } = 1;

    public bool IsDiscoverable { get; set; } = true;

    public DateTimeOffset? BoostedUntil { get; set; }

    public DateTimeOffset LastActiveAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<CandidateSkill> Skills { get; set; } = new List<CandidateSkill>();

    public ICollection<CandidateExperience> Experiences { get; set; } = new List<CandidateExperience>();

    public ICollection<CandidateEducation> Education { get; set; } = new List<CandidateEducation>();

    public ICollection<ShippedTitle> ShippedTitles { get; set; } = new List<ShippedTitle>();

    public ICollection<PortfolioLink> PortfolioLinks { get; set; } = new List<PortfolioLink>();
}
