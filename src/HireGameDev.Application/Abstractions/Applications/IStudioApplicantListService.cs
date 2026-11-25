using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

// Internal prototype. The actor must come from trusted authentication, never request binding.
// An empty result also covers inaccessible jobs, to avoid revealing their existence.
public interface IStudioApplicantListService
{
    Task<Result<IReadOnlyList<StudioApplicantListItem>>> ListAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default);
}

// Deliberately excludes Candidate identifiers, live profile data and snapshot contents.
public sealed record StudioApplicantListItem(Guid ApplicationId, ApplicationStatus Status, DateTimeOffset SubmittedAt);
