using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Applications;

// These records intentionally exclude database identities, private account settings and verification metadata.
public sealed record CandidateApplicationSnapshot(
    string FullName, string Headline, DisciplineCategory PrimaryCategory, string City,
    SeniorityLevel Seniority, decimal YearsExperience, string? Bio, CandidateAvailability Availability,
    bool OpenToRemote, bool WillingToRelocate, short? NoticePeriodDays, decimal? ExpectedCtcLpa,
    decimal? CurrentCtcLpa, IReadOnlyList<ApplicationSkillSnapshot> Skills,
    IReadOnlyList<ApplicationExperienceSnapshot> Experience, IReadOnlyList<ApplicationEducationSnapshot> Education,
    IReadOnlyList<ApplicationPortfolioSnapshot> Portfolio, IReadOnlyList<ApplicationGameSnapshot> ShippedGames);

public sealed record ApplicationSkillSnapshot(string Name, SkillProficiency Proficiency, decimal? YearsUsed, short? LastUsedYear, bool IsPrimary);
public sealed record ApplicationExperienceSnapshot(string Company, string Title, DateOnly StartDate, DateOnly? EndDate, bool IsCurrent, string? Description);
public sealed record ApplicationEducationSnapshot(string Institution, string Degree, string? Field, short? StartYear, short? EndYear);
public sealed record ApplicationPortfolioSnapshot(PortfolioLinkType LinkType, string Url, string? Title, IReadOnlyList<string> Skills);
public sealed record ApplicationGameSnapshot(string GameName, string StudioName, string Role, IReadOnlyList<string> Platforms, string? Engine, short? ReleaseYear, string? StoreUrl);
// Null requirements identify older snapshots where requirements were not recorded.
public sealed record ApplicationJobSkillSnapshot(string Name, SkillProficiency MinProficiency);
public sealed record JobApplicationSnapshot(string JobTitle, string StudioName, string? ScreeningQuestion, string? ScreeningAnswer,
    IReadOnlyList<ApplicationJobSkillSnapshot>? RequiredSkills = null);
public sealed record ApplicationSnapshotDraft(int Version, CandidateApplicationSnapshot Candidate, JobApplicationSnapshot Job, string? Phone);
