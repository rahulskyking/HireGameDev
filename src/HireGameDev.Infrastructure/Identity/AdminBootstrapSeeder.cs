using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Auth;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.Identity;

internal sealed class AdminBootstrapSeeder : IAdminBootstrapSeeder
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IOptions<AdminOptions> _options;
    private readonly ILogger<AdminBootstrapSeeder> _logger;

    public AdminBootstrapSeeder(
        AppDbContext dbContext,
        IAuditLogger auditLogger,
        IOptions<AdminOptions> options,
        ILogger<AdminBootstrapSeeder> logger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _options = options;
        _logger = logger;
    }

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var normalizedEmails = GetNormalizedBootstrapEmails();
        if (normalizedEmails.Length == 0)
        {
            return 0;
        }

        var adminRole = await EnsureAdminRoleAsync(cancellationToken);

        var matchingUsers = await _dbContext.Users
            .AsNoTracking()
            .Where(u =>
                u.IsActive &&
                u.DeletedAt == null &&
                u.EmailConfirmed &&
                u.NormalizedEmail != null &&
                normalizedEmails.Contains(u.NormalizedEmail))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);

        if (matchingUsers.Count == 0)
        {
            return 0;
        }

        var matchingIds = matchingUsers.Select(u => u.Id).ToArray();
        var existingAdminUserIds = (await _dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.RoleId == adminRole.Id && matchingIds.Contains(ur.UserId))
            .Select(ur => ur.UserId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var grantedCount = 0;
        foreach (var user in matchingUsers)
        {
            if (existingAdminUserIds.Contains(user.Id))
            {
                continue;
            }

            _dbContext.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = user.Id,
                RoleId = adminRole.Id
            });

            _auditLogger.Enqueue(new AuditLogEntry(
                Action: "system.admin_bootstrap.role_granted",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.System,
                ActorUserId: user.Id,
                Reason: "Granted admin role via Admin:BootstrapEmails startup configuration.",
                AfterData: new { Role = HireGameDevRoleNames.Admin }));

            grantedCount++;
        }

        if (grantedCount > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "AdminBootstrapSeeder granted '{AdminRole}' role to {GrantedCount} configured bootstrap account(s).",
                HireGameDevRoleNames.Admin,
                grantedCount);
        }

        return grantedCount;
    }

    public async Task<bool> EnsureBootstrapAdminForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        var normalizedEmails = GetNormalizedBootstrapEmails();
        if (normalizedEmails.Length == 0)
        {
            return false;
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive && u.DeletedAt == null && u.EmailConfirmed)
            .Select(u => new { u.Id, u.NormalizedEmail, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return false;
        }

        var normalizedUserEmail = !string.IsNullOrWhiteSpace(user.NormalizedEmail)
            ? user.NormalizedEmail
            : user.Email?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedUserEmail) ||
            !normalizedEmails.Contains(normalizedUserEmail, StringComparer.Ordinal))
        {
            return false;
        }

        var adminRole = await EnsureAdminRoleAsync(cancellationToken);

        var alreadyAdmin = await _dbContext.UserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == adminRole.Id, cancellationToken);

        if (alreadyAdmin)
        {
            return false;
        }

        _dbContext.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = adminRole.Id
        });

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "system.admin_bootstrap.role_granted",
            EntityType: "user",
            EntityId: user.Id.ToString(),
            ActorType: AuditActorType.System,
            ActorUserId: user.Id,
            Reason: "Granted admin role via Admin:BootstrapEmails upon verified account authentication.",
            AfterData: new { Role = HireGameDevRoleNames.Admin }));

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "AdminBootstrapSeeder granted '{AdminRole}' role to bootstrap user {UserId}.",
            HireGameDevRoleNames.Admin,
            user.Id);

        return true;
    }

    private string[] GetNormalizedBootstrapEmails()
    {
        var configured = _options.Value?.BootstrapEmails;
        if (configured is null || configured.Length == 0)
        {
            return Array.Empty<string>();
        }

        return configured
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<ApplicationRole> EnsureAdminRoleAsync(CancellationToken cancellationToken)
    {
        var normalizedAdmin = HireGameDevRoleNames.Admin.ToUpperInvariant();
        var adminRole = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.NormalizedName == normalizedAdmin, cancellationToken);

        if (adminRole is not null)
        {
            return adminRole;
        }

        adminRole = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = HireGameDevRoleNames.Admin,
            NormalizedName = normalizedAdmin,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };

        _dbContext.Roles.Add(adminRole);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return adminRole;
    }
}
