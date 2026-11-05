using System.Text.Json;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireGameDev.Infrastructure.Persistence.SeedData;

internal sealed class AppSettingsSeeder : IAppSettingsSeeder
{
    private const string SettingsResourceSuffix = "app_settings.seed.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _dbContext;
    private readonly ISettingsService _settingsService;
    private readonly IClock _clock;
    private readonly ILogger<AppSettingsSeeder> _logger;

    public AppSettingsSeeder(
        AppDbContext dbContext,
        ISettingsService settingsService,
        IClock clock,
        ILogger<AppSettingsSeeder> logger)
    {
        _dbContext = dbContext;
        _settingsService = settingsService;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var appliedMigrations = await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (!appliedMigrations.Any(m => m.EndsWith("M0_5_SettingsAuditConsent", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "Skipping M0.5 app_settings seed because migration M0_5_SettingsAuditConsent has not been applied yet. Run 'dotnet ef database update'.");
            return;
        }

        var seeds = LoadEmbeddedSettings();
        var existingKeys = await _dbContext.AppSettings
            .AsNoTracking()
            .Select(s => s.Key)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);
        var inserted = 0;
        var now = _clock.UtcNow;

        foreach (var seed in seeds)
        {
            var key = seed.Key.Trim();
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Encountered empty setting key in app_settings.seed.json.");
            }

            // Never overwrite live database values that an Admin may have already modified at runtime.
            if (existingSet.Contains(key))
            {
                continue;
            }

            _dbContext.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = seed.Value.GetRawText(),
                Description = string.IsNullOrWhiteSpace(seed.Description) ? null : seed.Description.Trim(),
                UpdatedBy = null,
                UpdatedAt = now
            });

            existingSet.Add(key);
            inserted++;
        }

        if (inserted > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _settingsService.InvalidateCache();

        _logger.LogInformation(
            "M0.5 app_settings verified: {TotalSeedKeys} baseline keys configured (newly inserted: {InsertedCount}).",
            seeds.Count,
            inserted);
    }

    private static List<AppSettingSeedEntry> LoadEmbeddedSettings()
    {
        var assembly = typeof(AppSettingsSeeder).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(SettingsResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Embedded seed manifest ending with '{SettingsResourceSuffix}' was not found in {assembly.GetName().Name}.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open stream for embedded resource '{resourceName}'.");

        var seeds = JsonSerializer.Deserialize<List<AppSettingSeedEntry>>(stream, JsonOptions);
        if (seeds is null || seeds.Count == 0)
        {
            throw new InvalidOperationException($"Seed manifest '{SettingsResourceSuffix}' deserialized to an empty list.");
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            if (!seenKeys.Add(seed.Key.Trim()))
            {
                throw new InvalidOperationException($"Duplicate key '{seed.Key}' in {SettingsResourceSuffix}.");
            }
        }

        return seeds;
    }

    private sealed record AppSettingSeedEntry(
        string Key,
        JsonElement Value,
        string? Description);
}
