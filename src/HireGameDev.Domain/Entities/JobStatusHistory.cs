using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class JobStatusHistory
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public JobStatus? PreviousStatus { get; set; }

    public JobStatus NewStatus { get; set; }

    public JobHistoryActorType ActorType { get; set; }

    public Guid? ActorUserId { get; set; }

    public string ReasonCode { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
