using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Rules;

public static class JobLifecycleRules
{
    public static bool CanAppearPublicly(Job job, StudioProfile studio, DateTimeOffset now) =>
        job.StudioId == studio.Id && studio.DeletedAt is null && studio.SuspendedAt is null &&
        studio.VerificationStatus == StudioVerificationStatus.Verified &&
        job.Status == JobStatus.Active && job.ExpiresAt.HasValue && job.ExpiresAt > now;

    public static bool NeedsExpiry(Job job, DateTimeOffset now) =>
        (job.Status is JobStatus.Active or JobStatus.Paused or JobStatus.Full) &&
        job.ExpiresAt.HasValue && job.ExpiresAt <= now;

    public static bool CanEdit(Job job) => job.Status is JobStatus.Draft or JobStatus.Paused;
    public static bool CanDelete(Job job) => job.Status == JobStatus.Draft && job.FirstPublishedAt is null;
    public static bool ConsumesActiveSlot(JobStatus status) => status is JobStatus.Active or JobStatus.Full;
}
