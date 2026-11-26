using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

// Internal prototype. The actor must come from trusted authentication, never request binding.
// An empty result also covers inaccessible jobs, to avoid revealing their existence.
public interface IStudioApplicantListService
{
    Task<Result<StudioApplicantPage>> ListAsync(Guid actorUserId, Guid jobId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default, ApplicationStatus? status = null);
}

// Offset pages are not a frozen snapshot: inserts or access changes can shift subsequent pages.
public sealed record StudioApplicantPage(IReadOnlyList<StudioApplicantListItem> Items, bool HasMore);

// Name comes only from the submitted snapshot, never the live Candidate profile.
public sealed record StudioApplicantListItem(Guid ApplicationId, ApplicationStatus Status, DateTimeOffset SubmittedAt, string? CandidateName, ApplicationFitScore? FitScore = null, bool UsesCurrentJobRequirements = false);
