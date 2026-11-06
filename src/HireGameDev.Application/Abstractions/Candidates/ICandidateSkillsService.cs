using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Candidates;

namespace HireGameDev.Application.Abstractions.Candidates;

public interface ICandidateSkillsService
{
    Task<IReadOnlyList<CandidateSkillItem>> GetSkillsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CandidateSkillItem>>> SaveSkillsAsync(
        SaveCandidateSkillsCommand command,
        CancellationToken cancellationToken = default);
}
