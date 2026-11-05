using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;

namespace HireGameDev.Infrastructure.Services;

internal sealed class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly FrozenSet<string> SensitivePropertyNames = new[]
    {
        "password",
        "passwordhash",
        "newpassword",
        "currentpassword",
        "securitystamp",
        "concurrencystamp",
        "token",
        "accesstoken",
        "refreshtoken",
        "resettoken",
        "secret",
        "clientsecret",
        "apikey",
        "code",
        "phonenumber",
        "phone",
        "rawresumetext"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly AppDbContext _dbContext;
    private readonly IClientIpAccessor _clientIpAccessor;
    private readonly IClock _clock;

    public AuditLogger(
        AppDbContext dbContext,
        IClientIpAccessor clientIpAccessor,
        IClock clock)
    {
        _dbContext = dbContext;
        _clientIpAccessor = clientIpAccessor;
        _clock = clock;
    }

    public void Enqueue(AuditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entity = BuildEntity(entry);
        _dbContext.AuditLogs.Add(entity);
    }

    public async Task LogAsync(
        AuditLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        Enqueue(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private AuditLog BuildEntity(AuditLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Action))
        {
            throw new ArgumentException("Audit action cannot be empty.", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.EntityType))
        {
            throw new ArgumentException("Audit entityType cannot be empty.", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.EntityId))
        {
            throw new ArgumentException("Audit entityId cannot be empty.", nameof(entry));
        }

        var normalizedReason = string.IsNullOrWhiteSpace(entry.Reason) ? null : entry.Reason.Trim();
        if ((entry.ActorType == AuditActorType.Admin || entry.RequireReason) && normalizedReason is null)
        {
            throw new ArgumentException(
                $"Audit reason is mandatory when ActorType is Admin or RequireReason is true (action: '{entry.Action}').",
                nameof(entry));
        }

        var resolvedIp = entry.Ip ?? _clientIpAccessor.GetClientIpAddress();
        if (resolvedIp is not null && resolvedIp.IsIPv4MappedToIPv6)
        {
            resolvedIp = resolvedIp.MapToIPv4();
        }

        return new AuditLog
        {
            ActorUserId = entry.ActorUserId,
            ActorType = entry.ActorType,
            Action = entry.Action.Trim(),
            EntityType = entry.EntityType.Trim(),
            EntityId = entry.EntityId.Trim(),
            Reason = normalizedReason,
            BeforeData = SerializeAndRedact(entry.BeforeData),
            AfterData = SerializeAndRedact(entry.AfterData),
            Ip = resolvedIp,
            CreatedAt = _clock.UtcNow
        };
    }

    private static string? SerializeAndRedact(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        JsonNode? node;
        if (payload is string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            try
            {
                node = JsonNode.Parse(rawJson);
            }
            catch (JsonException)
            {
                node = JsonValue.Create(rawJson);
            }
        }
        else
        {
            node = JsonSerializer.SerializeToNode(payload, JsonOptions);
        }

        if (node is null)
        {
            return null;
        }

        RedactNodeInPlace(node);
        return node.ToJsonString();
    }

    private static void RedactNodeInPlace(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var keys = obj.Select(kvp => kvp.Key).ToArray();
            foreach (var key in keys)
            {
                if (SensitivePropertyNames.Contains(key))
                {
                    obj[key] = "[REDACTED]";
                }
                else if (obj[key] is JsonNode child)
                {
                    RedactNodeInPlace(child);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                {
                    RedactNodeInPlace(item);
                }
            }
        }
    }
}
