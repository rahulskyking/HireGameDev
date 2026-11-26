using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Abstractions.Applications;

// actorUserId must come from trusted authentication; results are private to that candidate.
public interface IJobCardFitService
{
    Task<IReadOnlyDictionary<Guid, ApplicationFitScore>> GetAsync(Guid actorUserId, IReadOnlyList<Guid> jobIds, CancellationToken cancellationToken = default);
}
