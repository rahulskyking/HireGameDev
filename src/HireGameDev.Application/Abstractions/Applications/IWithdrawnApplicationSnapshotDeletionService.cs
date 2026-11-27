using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Applications;

// Development-only. Actor must come from trusted authentication; never request binding.
public interface IWithdrawnApplicationSnapshotDeletionService
{
    // True means deleted now; false means it was already deleted. Other outcomes fail closed.
    Task<Result<bool>> DeleteAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default);
}
