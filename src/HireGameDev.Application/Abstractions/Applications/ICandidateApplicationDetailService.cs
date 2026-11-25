using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

// Internal pilot only. actorUserId comes from trusted authentication.
public interface ICandidateApplicationDetailService
{
    Task<Result<CandidateApplicationDetail>> GetAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default);
}

// Deliberately excludes Studio member identities, notes and the Candidate's snapshot data.
public sealed record CandidateApplicationEvent(ApplicationStatus? PreviousStatus, ApplicationStatus NewStatus, DateTimeOffset OccurredAt);
public sealed record CandidateApplicationDetail(Guid ApplicationId, string JobTitle, string StudioName,
    ApplicationStatus Status, DateTimeOffset SubmittedAt, IReadOnlyList<CandidateApplicationEvent> Events, bool HasEarlierEvents);
