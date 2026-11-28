using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

// Live discoverable profile, not an application or submitted snapshot.
public sealed record StudioCandidateRecommendation(Guid CandidateId, string Name, string Headline, ApplicationFitScore Fit);
public sealed record StudioCandidateDetail(Guid JobId, Guid StudioId, string JobTitle, string Name, string Headline,
    DisciplineCategory Category, SeniorityLevel Seniority, decimal YearsExperience, bool OpenToRemote,
    bool WillingToRelocate, string? Bio, IReadOnlyList<ApplicationSkillSnapshot> Skills, ApplicationFitScore Fit);
public sealed record StudioCandidateRecommendationsPage(Guid JobId, Guid StudioId, string JobTitle, IReadOnlyList<StudioCandidateRecommendation> Items, RecommendationPolicy Policy);
public interface IStudioCandidateRecommendationService
{
    Task<Result<StudioCandidateRecommendationsPage>> GetAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default);
    Task<Result<StudioCandidateDetail>> GetDetailAsync(Guid actorUserId, Guid studioId, Guid jobId, Guid candidateId, CancellationToken cancellationToken = default);
}
