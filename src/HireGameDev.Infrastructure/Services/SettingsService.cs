using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Settings;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HireGameDev.Infrastructure.Services;

internal sealed class SettingsService : ISettingsService
{
    private const string CacheKey = "hgd:app_settings:snapshot:v1";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public SettingsService(
        AppDbContext dbContext,
        IMemoryCache cache,
        IAuditLogger auditLogger,
        IClock clock)
    {
        _dbContext = dbContext;
        _cache = cache;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AppSettingItem>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        return snapshot.OrderedItems;
    }

    public async Task<int> GetIntAsync(
        string key,
        int defaultValue,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        if (!snapshot.ByKey.TryGetValue(key, out var item))
        {
            return defaultValue;
        }

        try
        {
            using var doc = JsonDocument.Parse(item.RawJsonValue);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Number when doc.RootElement.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(doc.RootElement.GetString(), out var parsed) => parsed,
                _ => defaultValue
            };
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public async Task<decimal> GetDecimalAsync(
        string key,
        decimal defaultValue,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        if (!snapshot.ByKey.TryGetValue(key, out var item))
        {
            return defaultValue;
        }

        try
        {
            using var doc = JsonDocument.Parse(item.RawJsonValue);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Number when doc.RootElement.TryGetDecimal(out var d) => d,
                JsonValueKind.String when decimal.TryParse(doc.RootElement.GetString(), out var parsed) => parsed,
                _ => defaultValue
            };
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public async Task<bool> GetBoolAsync(
        string key,
        bool defaultValue,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        if (!snapshot.ByKey.TryGetValue(key, out var item))
        {
            return defaultValue;
        }

        try
        {
            using var doc = JsonDocument.Parse(item.RawJsonValue);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(doc.RootElement.GetString(), out var parsed) => parsed,
                _ => defaultValue
            };
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public async Task<string> GetStringAsync(
        string key,
        string defaultValue,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOrCreateSnapshotAsync(cancellationToken);
        if (!snapshot.ByKey.TryGetValue(key, out var item))
        {
            return defaultValue;
        }

        try
        {
            using var doc = JsonDocument.Parse(item.RawJsonValue);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString() ?? defaultValue,
                JsonValueKind.Null or JsonValueKind.Undefined => defaultValue,
                _ => doc.RootElement.GetRawText()
            };
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public async Task<Result<AppSettingItem>> UpdateSettingAsync(
        UpdateAppSettingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Key))
        {
            return Result.Failure<AppSettingItem>(
                Error.Validation("Settings.KeyRequired", "Setting key is required."));
        }

        if (command.AdminUserId == Guid.Empty)
        {
            return Result.Failure<AppSettingItem>(
                Error.Validation("Settings.ActorRequired", "Admin actor user ID is required."));
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<AppSettingItem>(
                Error.Validation("Settings.ReasonRequired", "A reason is mandatory when updating platform settings."));
        }

        if (string.IsNullOrWhiteSpace(command.JsonValue))
        {
            return Result.Failure<AppSettingItem>(
                Error.Validation("Settings.ValueRequired", "Setting JSON value cannot be empty."));
        }

        string normalizedJson;
        try
        {
            using var doc = JsonDocument.Parse(command.JsonValue);
            normalizedJson = doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return Result.Failure<AppSettingItem>(
                Error.Validation(
                    "Settings.InvalidJson",
                    "Setting value must be valid JSON (for example: 15, false, 1.5, or \"v2\")."));
        }

        var key = command.Key.Trim();
        var now = _clock.UtcNow;

        var existing = await _dbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        object? beforeData = null;
        if (existing is null)
        {
            existing = new AppSetting
            {
                Key = key,
                Value = normalizedJson,
                Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
                UpdatedBy = command.AdminUserId,
                UpdatedAt = now
            };

            _dbContext.AppSettings.Add(existing);
        }
        else
        {
            beforeData = new
            {
                key = existing.Key,
                value = JsonNode.Parse(existing.Value),
                description = existing.Description
            };

            existing.Value = normalizedJson;
            if (command.Description is not null)
            {
                existing.Description = string.IsNullOrWhiteSpace(command.Description)
                    ? null
                    : command.Description.Trim();
            }

            existing.UpdatedBy = command.AdminUserId;
            existing.UpdatedAt = now;
        }

        var afterData = new
        {
            key = existing.Key,
            value = JsonNode.Parse(normalizedJson),
            description = existing.Description
        };

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "settings.update",
            EntityType: "app_setting",
            EntityId: key,
            ActorType: AuditActorType.Admin,
            ActorUserId: command.AdminUserId,
            Reason: command.Reason.Trim(),
            BeforeData: beforeData,
            AfterData: afterData,
            RequireReason: true));

        await _dbContext.SaveChangesAsync(cancellationToken);
        InvalidateCache();

        return Result.Success(new AppSettingItem(
            existing.Key,
            existing.Value,
            existing.Description,
            existing.UpdatedBy,
            existing.UpdatedAt));
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
    }

    private async Task<CachedSettingsSnapshot> GetOrCreateSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out CachedSettingsSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await _dbContext.AppSettings
            .AsNoTracking()
            .OrderBy(s => s.Key)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new AppSettingItem(r.Key, r.Value, r.Description, r.UpdatedBy, r.UpdatedAt))
            .ToArray();

        var snapshot = new CachedSettingsSnapshot(
            items,
            items.ToFrozenDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase));

        // Strict 60-second absolute TTL (no sliding extension) so even direct DB updates take effect within <= 60s.
        _cache.Set(CacheKey, snapshot, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = RefreshInterval
        });

        return snapshot;
    }

    private sealed record CachedSettingsSnapshot(
        IReadOnlyList<AppSettingItem> OrderedItems,
        FrozenDictionary<string, AppSettingItem> ByKey);
}
