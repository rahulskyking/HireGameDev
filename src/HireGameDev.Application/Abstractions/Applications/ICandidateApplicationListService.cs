using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

// Internal pilot only; the actor must be obtained from trusted authentication.
public interface ICandidateApplicationListService
{
    Task<Result<IReadOnlyList<CandidateApplicationListItem>>> ListAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed record CandidateApplicationListItem(Guid ApplicationId, string JobTitle, string StudioName,
    ApplicationStatus Status, DateTimeOffset SubmittedAt);
