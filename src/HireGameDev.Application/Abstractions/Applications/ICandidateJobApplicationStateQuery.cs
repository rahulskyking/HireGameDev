using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

// Only the authenticated Candidate's application metadata; never the submitted snapshot.
public sealed record CandidateJobApplicationState(Guid ApplicationId, ApplicationStatus Status);
public interface ICandidateJobApplicationStateQuery
{
    Task<Result<IReadOnlyDictionary<Guid, CandidateJobApplicationState>>> GetAsync(Guid actorUserId, IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default);
}
