using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Candidates;

public sealed record CandidateExperienceItem(
    Guid Id,
    string CompanyName,
    string Title,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description);

public sealed record CandidateEducationItem(
    Guid Id,
    string Institution,
    string Degree,
    string? Field,
    short? StartYear,
    short? EndYear);

public sealed record CandidateBackgroundSnapshot(
    IReadOnlyList<CandidateExperienceItem> Experiences,
    IReadOnlyList<CandidateEducationItem> Education);

public sealed record CandidateExperienceInput(
    Guid? Id,
    string CompanyName,
    string Title,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Description);

public sealed record CandidateEducationInput(
    Guid? Id,
    string Institution,
    string Degree,
    string? Field,
    short? StartYear,
    short? EndYear);

public sealed record SaveCandidateStep2BackgroundCommand(
    Guid UserId,
    decimal YearsExperience,
    SeniorityLevel Seniority,
    IReadOnlyList<CandidateExperienceInput> Experiences,
    IReadOnlyList<CandidateEducationInput> Education);
