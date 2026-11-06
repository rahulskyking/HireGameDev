using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

public sealed record CandidateProfileSnapshot(
    Guid Id,
    Guid UserId,
    string FullName,
    string Headline,
    DisciplineCategory PrimaryCategory,
    int CityId,
    string CityName,
    string CityStateCode,
    string? PhotoPath,
    string? Phone,
    DateTimeOffset? PhoneVerifiedAt,
    decimal YearsExperience,
    SeniorityLevel Seniority,
    SeniorityLevel SuggestedSeniority,
    decimal? ExpectedCtcLpa,
    decimal? CurrentCtcLpa,
    bool ShowCurrentCtc,
    short? NoticePeriodDays,
    bool OpenToRemote,
    bool WillingToRelocate,
    CandidateAvailability Availability,
    string? Bio,
    short Completeness,
    short OnboardingStep,
    bool IsDiscoverable,
    DateTimeOffset UpdatedAt);

public sealed record CandidateOnboardingState(
    Guid UserId,
    string SuggestedFullName,
    CandidateProfileSnapshot? Profile,
    CandidateCompletenessBreakdown CompletenessBreakdown);

public sealed record SaveCandidateBasicsCommand(
    Guid UserId,
    string FullName,
    string Headline,
    DisciplineCategory PrimaryCategory,
    int CityId,
    string? Bio);

public sealed record SaveCandidateExperienceCoreCommand(
    Guid UserId,
    decimal YearsExperience,
    SeniorityLevel Seniority);

public sealed record SaveCandidatePreferencesCommand(
    Guid UserId,
    decimal ExpectedCtcLpa,
    decimal? CurrentCtcLpa,
    bool ShowCurrentCtc,
    short NoticePeriodDays,
    bool OpenToRemote,
    bool WillingToRelocate,
    CandidateAvailability Availability);
