using System.Net;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Audit;

public sealed record AuditLogEntry(
    string Action,
    string EntityType,
    string EntityId,
    AuditActorType ActorType = AuditActorType.User,
    Guid? ActorUserId = null,
    string? Reason = null,
    object? BeforeData = null,
    object? AfterData = null,
    IPAddress? Ip = null,
    bool RequireReason = false);
