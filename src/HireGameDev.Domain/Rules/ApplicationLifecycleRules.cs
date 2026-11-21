using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Rules;

/// <summary>
/// Pure lifecycle rules only. Callers must separately authorize the actor, check current
/// restrictions, use database concurrency control and atomically record every change.
/// </summary>
public static class ApplicationLifecycleRules
{
    public static bool CanStudioTransition(ApplicationStatus from, ApplicationStatus to) => (from, to) switch
    {
        (ApplicationStatus.Submitted, ApplicationStatus.Reviewing or ApplicationStatus.Shortlisted or ApplicationStatus.Rejected) => true,
        (ApplicationStatus.Reviewing, ApplicationStatus.Shortlisted or ApplicationStatus.Rejected) => true,
        (ApplicationStatus.Shortlisted, ApplicationStatus.Reviewing or ApplicationStatus.Rejected or ApplicationStatus.Hired) => true,
        _ => false
    };

    public static bool CanCandidateWithdraw(ApplicationStatus status) => status is
        ApplicationStatus.Submitted or ApplicationStatus.Reviewing or ApplicationStatus.Shortlisted or ApplicationStatus.Rejected;

    public static bool CountsTowardCapacity(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Submitted or ApplicationStatus.Reviewing or ApplicationStatus.Shortlisted or
            ApplicationStatus.Rejected or ApplicationStatus.Hired => true,
        ApplicationStatus.Withdrawn => false,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static bool MayRevealSnapshotPhone(ApplicationStatus status) => status is
        ApplicationStatus.Shortlisted or ApplicationStatus.Hired;
}
