using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Jobs;

// Public discovery has no authenticated mutations. Engagement features will use the stable IDs,
// separate authorized services and their own persistence when that module is approved.
public sealed record PublicJobSearch
{
    public string? Keyword { get; init; }
    public DisciplineCategory? Category { get; init; }
    public JobType? JobType { get; init; }
    public JobWorkMode? WorkMode { get; init; }
    public int? CityId { get; init; }
    public decimal? ExperienceYears { get; init; }
    public IReadOnlyList<int> SkillIds { get; init; } = Array.Empty<int>();
    public bool FresherFriendlyOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record PublicJobStudio(Guid Id, string Name, string Slug, string? LogoUrl);
public sealed record PublicJobCard(Guid Id, string Slug, string Title, PublicJobStudio Studio,
    JobType JobType, JobWorkMode WorkMode, string? CityName, string? CityStateCode,
    decimal SalaryMin, decimal SalaryMax, JobPayUnit PayUnit, decimal MinYears, decimal? MaxYears,
    bool FresherFriendly, DateTimeOffset PublishedAt, DateTimeOffset ExpiresAt);
public sealed record PublicJobSearchPage(IReadOnlyList<PublicJobCard> Items, int TotalCount,
    int Page, int PageSize, int TotalPages);
public sealed record PublicJobSkill(int Id, string Name, bool IsRequired, SkillProficiency MinProficiency);
public sealed record PublicJobDetail(PublicJobCard Summary, DisciplineCategory Category,
    SeniorityLevel SeniorityMin, SeniorityLevel SeniorityMax, string Description,
    bool RelocationAssistance, short Openings, short? MaxNoticeDays, string? ScreeningQuestion,
    IReadOnlyList<PublicJobSkill> Skills);
