using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Rules;

/// <summary>
/// Planning calculation only. This does not delete data or authorize a worker. A future caller
/// must re-read and lock the job and application, validate history, and coordinate renewal,
/// withdrawal, readers, attachments, backups, legal holds and audit before deleting anything.
/// </summary>
public static class ApplicationRetentionRules
{
    public static DateTimeOffset? SnapshotDeletionDueAt(
        ApplicationStatus applicationStatus, DateTimeOffset? withdrawnAt,
        JobStatus jobStatus, DateTimeOffset? expiresAt, DateTimeOffset? closedAt,
        DateTimeOffset? removedAt, DateTimeOffset now)
    {
        if (!Enum.IsDefined(applicationStatus) || !Enum.IsDefined(jobStatus) || jobStatus == JobStatus.Draft)
            return null;
        // Missing required terminal metadata is not permission to delete.
        if ((applicationStatus == ApplicationStatus.Withdrawn && withdrawnAt is null) ||
            (jobStatus == JobStatus.Closed && closedAt is null) ||
            (jobStatus == JobStatus.Removed && removedAt is null) ||
            (jobStatus == JobStatus.Expired && expiresAt is null))
            return null;

        var nowUtc = now.ToUniversalTime();
        var starts = new List<DateTimeOffset>();
        if (applicationStatus == ApplicationStatus.Withdrawn && withdrawnAt is { } withdrawal)
            starts.Add(withdrawal.ToUniversalTime());
        if (closedAt is { } closed) starts.Add(closed.ToUniversalTime());
        if (removedAt is { } removed) starts.Add(removed.ToUniversalTime());

        // Closing/removing a job before its expiry prevents the future expiry from becoming
        // an earlier event. A renewal replaces ExpiresAt; while it is in the future there is
        // no expiry countdown, unless withdrawal or a terminal event independently applies.
        if (expiresAt is { } expiry && expiry <= nowUtc &&
            (!closedAt.HasValue || expiry <= closedAt.Value) &&
            (!removedAt.HasValue || expiry <= removedAt.Value))
            starts.Add(expiry.ToUniversalTime());
        if (starts.Count == 0) return null;
        return starts.Min().AddMonths(12);
    }
}
