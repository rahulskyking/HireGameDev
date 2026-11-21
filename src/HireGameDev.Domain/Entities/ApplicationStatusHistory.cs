using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class ApplicationStatusHistory
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public ApplicationStatus? PreviousStatus { get; set; }
    public ApplicationStatus NewStatus { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
