using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Plans;

// Configuration draft only. These values do not enforce or count searches yet.
public sealed record SearchAllowanceDraft(int? CandidateDaily, int? StudioDaily);
public interface ISearchAllowanceDraftService
{
    Task<Result<SearchAllowanceDraft>> GetAsync(CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(Guid actorUserId, SearchAllowanceDraft draft, string reason, CancellationToken cancellationToken = default);
}
