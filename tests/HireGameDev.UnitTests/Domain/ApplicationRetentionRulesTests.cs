using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class ApplicationRetentionRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Start.AddMonths(13);

    [Fact]
    public void EarliestWithdrawalOrClosureStartsTwelveMonths()
    {
        Assert.Equal(Start.AddMonths(12), ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Withdrawn, Start, JobStatus.Closed, null, Start.AddDays(20), null, Now));
        Assert.Equal(Start.AddMonths(12), ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Withdrawn, Start.AddDays(20), JobStatus.Closed, null, Start, null, Now));
    }

    [Fact]
    public void HiredUsesClosureAndRemovalIsTerminal()
    {
        Assert.Equal(Start.AddMonths(12), ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Hired, null, JobStatus.Closed, null, Start, null, Now));
        Assert.Equal(Start.AddMonths(12), ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Submitted, null, JobStatus.Removed, null, null, Start, Now));
    }

    [Fact]
    public void ExpiryStartsCountdown_AndRenewalStopsItUntilNewExpiry()
    {
        Assert.Equal(Start.AddMonths(12), ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Submitted, null, JobStatus.Expired, Start, null, null, Now));
        Assert.Null(ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Submitted, null, JobStatus.Active, Now.AddDays(10), null, null, Now));
        Assert.Equal(Start.AddMonths(12), ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Withdrawn, Start, JobStatus.Active, Now.AddDays(10), null, null, Now));
    }

    [Fact]
    public void ExpiryBeforeClosingCounts_ButClosingBeforeExpiryWins()
    {
        Assert.Equal(Start.AddMonths(12), ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Submitted, null, JobStatus.Closed, Start, Start.AddDays(20), null, Now));
        Assert.Equal(Start.AddMonths(12), ApplicationRetentionRules.SnapshotDeletionDueAt(
            ApplicationStatus.Submitted, null, JobStatus.Closed, Start.AddDays(20), Start, null, Now));
    }

    [Fact]
    public void MissingRequiredMetadataFailsClosed()
    {
        Assert.Null(ApplicationRetentionRules.SnapshotDeletionDueAt(ApplicationStatus.Withdrawn, null, JobStatus.Closed, Start, Start, null, Now));
        Assert.Null(ApplicationRetentionRules.SnapshotDeletionDueAt(ApplicationStatus.Hired, null, JobStatus.Closed, Start, null, null, Now));
        Assert.Null(ApplicationRetentionRules.SnapshotDeletionDueAt(ApplicationStatus.Submitted, null, JobStatus.Removed, Start, null, null, Now));
        Assert.Null(ApplicationRetentionRules.SnapshotDeletionDueAt(ApplicationStatus.Submitted, null, JobStatus.Expired, null, null, null, Now));
        Assert.Null(ApplicationRetentionRules.SnapshotDeletionDueAt(ApplicationStatus.Submitted, null, JobStatus.Draft, Start, null, null, Now));
    }
}
