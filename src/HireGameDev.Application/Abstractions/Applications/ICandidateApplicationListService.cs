using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Applications;

// The actor must be obtained from trusted authentication.
public interface ICandidateApplicationListService
{
    Task<Result<CandidateApplicationPage>> ListAsync(Guid actorUserId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default, ApplicationStatus? status = null);
}

// Offset pages may shift when applications are added or removed between requests.
public sealed record CandidateApplicationPage(IReadOnlyList<CandidateApplicationListItem> Items, int Page, bool HasMore);

public sealed record CandidateApplicationListItem(Guid ApplicationId, string JobTitle, string StudioName,
    ApplicationStatus Status, DateTimeOffset SubmittedAt);
