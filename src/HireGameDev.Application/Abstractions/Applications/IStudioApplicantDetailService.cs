using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

// Internal prototype. actorUserId must come from trusted authentication, never request binding.
public interface IStudioApplicantDetailService
{
    Task<Result<StudioApplicantDetail>> GetAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default);
}

// Only the consented submission snapshot is returned; never a live Candidate profile.
public sealed record StudioApplicantDetail(Guid ApplicationId, ApplicationStatus Status, DateTimeOffset SubmittedAt,
    CandidateApplicationSnapshot Candidate, JobApplicationSnapshot Job, string? Phone);
