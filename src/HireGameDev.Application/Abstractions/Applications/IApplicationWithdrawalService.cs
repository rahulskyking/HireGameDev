using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Abstractions.Applications;

/// <summary>Actor must come from trusted authentication, never from a request body.</summary>
public interface IApplicationWithdrawalService
{
    Task<Result<ApplicationWithdrawalResult>> WithdrawAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default);
}
