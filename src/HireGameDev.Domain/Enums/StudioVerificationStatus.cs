namespace HireGameDev.Domain.Enums;

/// <summary>
/// Verification lifecycle stored in <c>studio_profiles.verification_status</c>
/// (<c>'unverified'</c>, <c>'pending'</c>, <c>'verified'</c>, <c>'rejected'</c>).
/// </summary>
public enum StudioVerificationStatus
{
    Unverified = 0,
    Pending = 1,
    Verified = 2,
    Rejected = 3
}

public static class StudioVerificationStatusExtensions
{
    public static string ToDisplayLabel(this StudioVerificationStatus status) => status switch
    {
        StudioVerificationStatus.Unverified => "Unverified",
        StudioVerificationStatus.Pending => "Verification Pending",
        StudioVerificationStatus.Verified => "Verified Studio",
        StudioVerificationStatus.Rejected => "Verification Needs Changes",
        _ => "Unverified"
    };
}
