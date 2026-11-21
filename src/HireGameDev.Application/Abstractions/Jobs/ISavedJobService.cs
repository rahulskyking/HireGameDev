using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;

namespace HireGameDev.Application.Abstractions.Jobs;

/// <summary>Actor ID is trusted server authentication context, never bound from client input.</summary>
public interface ISavedJobService
{
    Task<Result> SaveAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default);
    Task<Result> RemoveAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default);
    Task<Result<SavedJobPage>> ListAsync(Guid actorUserId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}
