using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Abstractions.Applications;

/// <summary>Private review only; the result is not a consent receipt or permission to submit.</summary>
public interface IApplicationReviewService
{
    // actorUserId must be supplied by trusted authentication, not request binding.
    Task<Result<ApplicationSnapshotDraft>> ReviewAsync(Guid actorUserId, Guid jobId, string? screeningAnswer, CancellationToken cancellationToken = default);
}
