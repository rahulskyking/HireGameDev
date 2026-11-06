using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;

namespace HireGameDev.Application.Abstractions.Candidates;

public interface ICandidateBackgroundService
{
    Task<CandidateBackgroundSnapshot> GetBackgroundForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<CandidateBackgroundSnapshot>> SaveStep2BackgroundAsync(
        SaveCandidateStep2BackgroundCommand command,
        CancellationToken cancellationToken = default);
}
