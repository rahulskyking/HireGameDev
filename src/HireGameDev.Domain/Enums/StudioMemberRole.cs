namespace HireGameDev.Domain.Enums;

/// <summary>
/// Studio workspace RBAC role stored in <c>studio_members.member_role</c>
/// (<c>'owner'</c>, <c>'recruiter'</c>, <c>'viewer'</c>).
/// </summary>
public enum StudioMemberRole
{
    Owner = 0,
    Recruiter = 1,
    Viewer = 2
}

public static class StudioMemberRoleExtensions
{
    public static string ToDisplayLabel(this StudioMemberRole role) => role switch
    {
        StudioMemberRole.Owner => "Owner",
        StudioMemberRole.Recruiter => "Recruiter",
        StudioMemberRole.Viewer => "Viewer",
        _ => "Viewer"
    };
}
