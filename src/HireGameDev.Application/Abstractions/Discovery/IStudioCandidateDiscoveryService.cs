using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Discovery;

public sealed record CandidateDiscoverySearch
{
    public string? Keyword { get; init; }
    public DisciplineCategory? Category { get; init; }
    public int? CityId { get; init; }
    public int? SkillId { get; init; }
    public decimal? MinimumYears { get; init; }
    public bool RemoteOnly { get; init; }
    public int Page { get; init; } = 1;
}
public sealed record CandidateDiscoveryItem(Guid Id, string Name, string Headline, DisciplineCategory Category,
    SeniorityLevel Seniority, decimal YearsExperience, string? City, CandidateAvailability Availability, bool OpenToRemote);
public sealed record CandidateDiscoveryPage(IReadOnlyList<CandidateDiscoveryItem> Items, int Page, bool HasMore);
public sealed record CandidateDiscoverySkill(string Name, SkillProficiency Proficiency);
public sealed record CandidateDiscoveryDetail(Guid Id, string Name, string Headline, DisciplineCategory Category,
    SeniorityLevel Seniority, decimal YearsExperience, string? City, CandidateAvailability Availability,
    bool OpenToRemote, bool WillingToRelocate, string? Bio, IReadOnlyList<CandidateDiscoverySkill> Skills);

// Studio discovery is live, not an application snapshot. Actor and Studio are trusted server values.
public interface IStudioCandidateDiscoveryService
{
    Task<Result<CandidateDiscoveryPage>> SearchAsync(Guid actorUserId, Guid studioId, CandidateDiscoverySearch search, CancellationToken cancellationToken = default);
    Task<Result<CandidateDiscoveryDetail>> GetAsync(Guid actorUserId, Guid studioId, Guid candidateId, CancellationToken cancellationToken = default);
}
