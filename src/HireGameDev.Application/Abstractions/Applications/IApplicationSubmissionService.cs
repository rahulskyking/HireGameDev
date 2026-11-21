using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Abstractions.Applications;

/// <summary>Caller derives the actor from trusted authentication; never bind it from request data.</summary>
public interface IApplicationSubmissionService
{
    Task<Result<ApplicationReviewOffer>> PrepareAsync(Guid actorUserId, Guid jobId, string? screeningAnswer, CancellationToken cancellationToken = default);
    Task<Result<SubmittedApplication>> SubmitAsync(SubmitApplicationCommand command, CancellationToken cancellationToken = default);
}
