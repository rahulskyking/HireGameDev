using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class ApplicationLifecycleRulesTests
{
    [Fact]
    public void StudioTransitions_ExactlyMatchAgreedMatrix()
    {
        var allowed = new HashSet<(ApplicationStatus, ApplicationStatus)>
        {
            (ApplicationStatus.Submitted, ApplicationStatus.Reviewing),
            (ApplicationStatus.Submitted, ApplicationStatus.Shortlisted),
            (ApplicationStatus.Submitted, ApplicationStatus.Rejected),
            (ApplicationStatus.Reviewing, ApplicationStatus.Shortlisted),
            (ApplicationStatus.Reviewing, ApplicationStatus.Rejected),
            (ApplicationStatus.Shortlisted, ApplicationStatus.Reviewing),
            (ApplicationStatus.Shortlisted, ApplicationStatus.Rejected),
            (ApplicationStatus.Shortlisted, ApplicationStatus.Hired)
        };
        foreach (var from in Enum.GetValues<ApplicationStatus>())
        foreach (var to in Enum.GetValues<ApplicationStatus>())
            Assert.Equal(allowed.Contains((from, to)), ApplicationLifecycleRules.CanStudioTransition(from, to));
    }

    [Fact]
    public void WithdrawalAndCapacity_RespectFinalStates()
    {
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            Assert.Equal(status is not (ApplicationStatus.Hired or ApplicationStatus.Withdrawn), ApplicationLifecycleRules.CanCandidateWithdraw(status));
            Assert.Equal(status != ApplicationStatus.Withdrawn, ApplicationLifecycleRules.CountsTowardCapacity(status));
            Assert.Equal(status is ApplicationStatus.Shortlisted or ApplicationStatus.Hired, ApplicationLifecycleRules.MayRevealSnapshotPhone(status));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => ApplicationLifecycleRules.CountsTowardCapacity((ApplicationStatus)999));
        Assert.False(ApplicationLifecycleRules.CanCandidateWithdraw((ApplicationStatus)999));
        Assert.False(ApplicationLifecycleRules.CanStudioTransition((ApplicationStatus)999, ApplicationStatus.Hired));
        Assert.False(ApplicationLifecycleRules.MayRevealSnapshotPhone((ApplicationStatus)999));
    }
}
