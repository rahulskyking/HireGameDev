using System.Net;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

/// <summary>
/// Append-only security and compliance audit trail entry in <c>audit_logs</c>.
/// Admin actions (<see cref="AuditActorType.Admin"/>) require a non-empty <see cref="Reason"/>.
/// </summary>
public sealed class AuditLog
{
    public long Id { get; set; }

    public Guid? ActorUserId { get; set; }

    public AuditActorType ActorType { get; set; } = AuditActorType.User;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string? Reason { get; set; }

    public string? BeforeData { get; set; }

    public string? AfterData { get; set; }

    public IPAddress? Ip { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
