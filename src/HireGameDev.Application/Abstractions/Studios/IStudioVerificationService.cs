using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Studios;

public interface IStudioVerificationService
{
    Task<Result<StudioVerificationWorkspaceModel>> GetVerificationWorkspaceAsync(
        Guid actorUserId,
        Guid? requestedStudioId = null,
        CancellationToken cancellationToken = default);

    Task<Result<StudioVerificationWorkspaceModel>> SubmitForVerificationAsync(
        SubmitStudioVerificationCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<StudioVerificationWorkspaceModel>> RequestIdentityChangeAsync(
        RequestStudioIdentityChangeCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<StudioVerificationWorkspaceModel>> CancelIdentityChangeAsync(
        CancelStudioIdentityChangeCommand command,
        CancellationToken cancellationToken = default);

    Task<AdminStudioVerificationQueueModel> GetAdminQueueAsync(
        StudioVerificationStatus? statusFilter = StudioVerificationStatus.Pending,
        CancellationToken cancellationToken = default);

    Task<Result<ReviewStudioVerificationResult>> ReviewVerificationAsync(
        ReviewStudioVerificationCommand command,
        CancellationToken cancellationToken = default);
}
