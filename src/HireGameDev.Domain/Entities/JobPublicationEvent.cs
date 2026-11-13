using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

/// <summary>Append-only through lifecycle services; retains published fingerprints after edits or closure.</summary>
public sealed class JobPublicationEvent
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public Guid StudioId { get; set; }

    public Guid ActorUserId { get; set; }

    public JobPublicationKind Kind { get; set; }

    public string NormalizedTitle { get; set; } = string.Empty;

    public string ContentFingerprint { get; set; } = string.Empty;

    public DateTimeOffset PublishedAt { get; set; }
}
