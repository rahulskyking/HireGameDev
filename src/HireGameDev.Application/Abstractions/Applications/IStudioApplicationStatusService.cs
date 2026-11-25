using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

/// <summary>Actor is supplied by trusted authentication. Studio is resolved from the application.</summary>
public interface IStudioApplicationStatusService
{
    Task<Result> ChangeStatusAsync(Guid actorUserId, Guid applicationId, ApplicationStatus newStatus, CancellationToken cancellationToken = default);
}
