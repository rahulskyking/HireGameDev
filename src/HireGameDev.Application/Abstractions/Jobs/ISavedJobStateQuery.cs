using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Jobs;

/// <summary>Looks up the signed-in Candidate's saved state for a bounded set of job IDs.</summary>
public interface ISavedJobStateQuery
{
    Task<Result<IReadOnlySet<Guid>>> GetSavedIdsAsync(Guid actorUserId, IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default);
}
