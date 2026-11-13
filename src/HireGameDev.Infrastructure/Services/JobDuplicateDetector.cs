using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.Services;

/// <summary>Read-only check; publication callers must hold the Studio lock to prevent concurrent duplicates.</summary>
internal sealed class JobDuplicateDetector(AppDbContext db, IClock clock, IOptions<JobLifecycleOptions> options)
{
    public async Task<JobPublishFinding?> CheckAsync(JobPublishContext context, CancellationToken ct)
    {
        var title = JobContentIdentity.NormalizedTitle(context.Content);
        var fingerprint = JobContentIdentity.Fingerprint(context.Content);
        var now = clock.UtcNow.ToUniversalTime();
        var candidates = await db.Jobs.AsNoTracking().Where(x => x.StudioId == context.StudioId && x.Id != context.JobId &&
            (x.Status == JobStatus.Active || x.Status == JobStatus.Paused || x.Status == JobStatus.Full) && x.ExpiresAt > now)
            .Select(x => new { x.NormalizedTitle, x.ContentFingerprint, x.CityId, x.WorkMode }).ToArrayAsync(ct);
        if (candidates.Any(x => x.ContentFingerprint == fingerprint ||
            (x.NormalizedTitle == title && x.CityId == context.Content.CityId && x.WorkMode == context.Content.WorkMode)))
            return new("Jobs.Duplicate", JobFindingSeverity.PostingRestriction, "Title", "An equivalent studio listing already exists. Update its openings instead.");
        var cutoff = now.AddDays(-options.Value.RepeatCooldownDays);
        if (await db.JobPublicationEvents.AnyAsync(x => x.StudioId == context.StudioId && x.JobId != context.JobId &&
            x.ContentFingerprint == fingerprint && x.PublishedAt > cutoff, ct))
            return new("Jobs.RepeatCooldown", JobFindingSeverity.PostingRestriction, "Title", "Recently published identical content cannot be reposted yet, even after closing or removal.");
        return null;
    }
}
