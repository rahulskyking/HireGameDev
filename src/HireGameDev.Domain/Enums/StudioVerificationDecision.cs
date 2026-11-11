namespace HireGameDev.Domain.Enums;

/// <summary>
/// Admin moderation actions in the Studio Verification review queue (<c>/Admin/Studios/Verification</c>).
/// </summary>
public enum StudioVerificationDecision
{
    Approve = 0,
    Reject = 1,
    Revoke = 2
}
