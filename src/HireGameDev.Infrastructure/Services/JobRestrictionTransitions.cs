using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

internal static class JobRestrictionTransitions
{
    // Caller owns the transaction and Studio lock; save with the restriction and Admin audit event.
    internal static async Task PauseActiveJobsAsync(AppDbContext db, Guid studioId, Guid adminUserId,
        DateTimeOffset now, string code, string reason, CancellationToken ct)
    {
        var jobs = await db.Jobs.Where(x => x.StudioId == studioId && x.Status == JobStatus.Active)
            .OrderBy(x => x.Id).ToArrayAsync(ct);
        foreach (var job in jobs)
        {
            var expired = JobLifecycleRules.NeedsExpiry(job, now);
            job.Status = expired ? JobStatus.Expired : JobStatus.Paused;
            job.UpdatedAt = now;
            db.JobStatusHistory.Add(new JobStatusHistory
            {
                Id = Guid.NewGuid(), JobId = job.Id, PreviousStatus = JobStatus.Active, NewStatus = job.Status,
                ActorType = expired ? JobHistoryActorType.System : JobHistoryActorType.User,
                ActorUserId = expired ? null : adminUserId, OccurredAt = now,
                ReasonCode = expired ? "validity_expired" : code, Reason = expired ? null : reason
            });
        }
    }
}
