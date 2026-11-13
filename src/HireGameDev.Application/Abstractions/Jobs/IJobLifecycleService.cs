using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;

namespace HireGameDev.Application.Abstractions.Jobs;

public interface IJobLifecycleService
{
    Task<Result<StudioJobList>> ListAsync(Guid actorUserId, Guid studioId, HireGameDev.Domain.Enums.JobStatus? status = null, int page = 1, CancellationToken cancellationToken = default);
    Task<Result<JobSnapshot>> GetAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default);
    Task<Result<JobSnapshot>> SaveAsync(SaveJobCommand command, CancellationToken cancellationToken = default);
    Task<Result> DeleteDraftAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default);
    Task<Result<JobLifecycleOutcome>> TransitionAsync(JobTransitionCommand command, CancellationToken cancellationToken = default);
    Task<Result> RemoveAsync(RemoveJobCommand command, CancellationToken cancellationToken = default);
    Task<Result> RestrictStudioAsync(StudioJobRestrictionCommand command, CancellationToken cancellationToken = default);
    // Trusted background entry point; do not expose directly as an unauthenticated HTTP action.
    Task<int> ExpireDueJobsAsync(int batchSize = 100, CancellationToken cancellationToken = default);
}

public interface IJobPublishChecker
{
    Task<JobPublishCheckResult> CheckAsync(JobPublishContext context, CancellationToken cancellationToken = default);
}
