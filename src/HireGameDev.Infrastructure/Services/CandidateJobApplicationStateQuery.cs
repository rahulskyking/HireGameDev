using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

internal sealed class CandidateJobApplicationStateQuery(AppDbContext db) : ICandidateJobApplicationStateQuery
{
    public async Task<Result<IReadOnlyDictionary<Guid, CandidateJobApplicationState>>> GetAsync(Guid actorUserId, IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        if (jobIds.Count > 50 || jobIds.Any(id => id == Guid.Empty))
            return Result.Failure<IReadOnlyDictionary<Guid, CandidateJobApplicationState>>(Error.Validation("Applications.InvalidJobs", "Choose up to 50 valid jobs."));
        var candidateId = await (from candidate in db.CandidateProfiles.AsNoTracking()
            join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
            where actorUserId != Guid.Empty && user.Id == actorUserId && user.IsActive && user.DeletedAt == null &&
                user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
            select (Guid?)candidate.Id).SingleOrDefaultAsync(cancellationToken);
        if (!candidateId.HasValue)
            return Result.Failure<IReadOnlyDictionary<Guid, CandidateJobApplicationState>>(Error.Forbidden("Applications.Forbidden", "An active Candidate account is required."));
        var ids = jobIds.Distinct().ToArray();
        var rows = await db.Applications.AsNoTracking().Where(a => a.CandidateId == candidateId.Value && ids.Contains(a.JobId))
            .Select(a => new { a.JobId, a.Id, a.Status }).ToArrayAsync(cancellationToken);
        return Result.Success<IReadOnlyDictionary<Guid, CandidateJobApplicationState>>(rows.ToDictionary(x => x.JobId, x => new CandidateJobApplicationState(x.Id, x.Status)));
    }
}
