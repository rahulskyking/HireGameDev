using System.Net;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Consent;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Features.Consent;
using HireGameDev.Application.Features.Settings;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

internal sealed class ConsentService : IConsentService
{
    private readonly AppDbContext _dbContext;
    private readonly ISettingsService _settingsService;
    private readonly IClientIpAccessor _clientIpAccessor;
    private readonly IClock _clock;

    public ConsentService(
        AppDbContext dbContext,
        ISettingsService settingsService,
        IClientIpAccessor clientIpAccessor,
        IClock clock)
    {
        _dbContext = dbContext;
        _settingsService = settingsService;
        _clientIpAccessor = clientIpAccessor;
        _clock = clock;
    }

    public async Task RecordConsentAsync(
        Guid userId,
        ConsentType consentType,
        string version,
        IPAddress? ip = null,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Consent version cannot be empty.", nameof(version));
        }

        var resolvedIp = NormalizeIp(ip ?? _clientIpAccessor.GetClientIpAddress());

        _dbContext.ConsentRecords.Add(new ConsentRecord
        {
            UserId = userId,
            ConsentType = consentType,
            Version = version.Trim(),
            AcceptedAt = _clock.UtcNow,
            Ip = resolvedIp
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordRegistrationConsentsAsync(
        Guid userId,
        IPAddress? ip = null,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        var termsVersion = await _settingsService.GetStringAsync(
            AppSettingKeys.LegalTermsVersion,
            AppSettingKeys.DefaultLegalVersion,
            cancellationToken);

        var privacyVersion = await _settingsService.GetStringAsync(
            AppSettingKeys.LegalPrivacyVersion,
            AppSettingKeys.DefaultLegalVersion,
            cancellationToken);

        var now = _clock.UtcNow;
        var resolvedIp = NormalizeIp(ip ?? _clientIpAccessor.GetClientIpAddress());

        _dbContext.ConsentRecords.AddRange(
            new ConsentRecord
            {
                UserId = userId,
                ConsentType = ConsentType.Terms,
                Version = termsVersion.Trim(),
                AcceptedAt = now,
                Ip = resolvedIp
            },
            new ConsentRecord
            {
                UserId = userId,
                ConsentType = ConsentType.Privacy,
                Version = privacyVersion.Trim(),
                AcceptedAt = now,
                Ip = resolvedIp
            });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasAcceptedVersionAsync(
        Guid userId,
        ConsentType consentType,
        string requiredVersion,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(requiredVersion))
        {
            return false;
        }

        var trimmedVersion = requiredVersion.Trim();

        return await _dbContext.ConsentRecords
            .AsNoTracking()
            .AnyAsync(
                c => c.UserId == userId
                     && c.ConsentType == consentType
                     && c.Version == trimmedVersion,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ConsentRecordItem>> GetUserConsentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return [];
        }

        var rows = await _dbContext.ConsentRecords
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.AcceptedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ConsentRecordItem(
                r.Id,
                r.UserId,
                r.ConsentType,
                r.Version,
                r.AcceptedAt,
                r.Ip))
            .ToArray();
    }

    private static IPAddress? NormalizeIp(IPAddress? ip)
        => ip is not null && ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
}
