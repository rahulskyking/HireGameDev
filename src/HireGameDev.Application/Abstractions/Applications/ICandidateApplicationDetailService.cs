using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Abstractions.Applications;

// actorUserId comes from trusted authentication.
public interface ICandidateApplicationDetailService
{
    Task<Result<CandidateApplicationDetail>> GetAsync(Guid actorUserId, Guid applicationId, CancellationToken cancellationToken = default);
}

// Deliberately excludes Studio member identities and private notes. Snapshot data is available only to its owner.
public sealed record CandidateApplicationEvent(ApplicationStatus? PreviousStatus, ApplicationStatus NewStatus, DateTimeOffset OccurredAt);
public sealed record CandidateApplicationDetail(Guid ApplicationId, string JobTitle, string StudioName,
    ApplicationStatus Status, DateTimeOffset SubmittedAt, IReadOnlyList<CandidateApplicationEvent> Events, bool HasEarlierEvents, ApplicationSkillMatch? SkillMatch = null, ApplicationFitScore? FitScore = null, bool UsesCurrentJobRequirements = false, ApplicationSnapshotDraft? SubmittedSnapshot = null, bool SnapshotPurged = false);
