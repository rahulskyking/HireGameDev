using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class StudioMember
{
    public Guid StudioId { get; set; }

    public StudioProfile Studio { get; set; } = null!;

    public Guid UserId { get; set; }

    public StudioMemberRole MemberRole { get; set; } = StudioMemberRole.Viewer;

    public DateTimeOffset InvitedAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
}
