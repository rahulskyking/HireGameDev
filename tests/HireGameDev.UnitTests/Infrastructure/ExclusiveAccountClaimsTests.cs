using System.Security.Claims;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HireGameDev.UnitTests.Infrastructure;

public sealed class ExclusiveAccountClaimsTests
{
    [Theory]
    [InlineData(AccountType.Candidate, "candidate", "studio")]
    [InlineData(AccountType.Studio, "studio", "candidate")]
    public async Task LegacyDualRoles_OnlyPrimaryWorkspaceIsAuthorized_AdminIsPreserved(
        AccountType accountType, string primaryRole, string oppositeRole)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HireGameDev.Application.Abstractions.IClock, HireGameDev.Infrastructure.Services.SystemClock>();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentity<ApplicationUser, ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var name in new[] { primaryRole, oppositeRole, HireGameDevRoleNames.Admin })
        {
            Assert.True((await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = name })).Succeeded);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "legacy@example.com", Email = "legacy@example.com",
            AccountType = accountType, DisplayName = "Legacy account"
        };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRolesAsync(user, new[] { primaryRole, oppositeRole, HireGameDevRoleNames.Admin })).Succeeded);
        var factory = new ApplicationUserClaimsPrincipalFactory(users, roles,
            scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>());
        var principal = await factory.CreateAsync(user);

        Assert.True(principal.IsInRole(primaryRole));
        Assert.False(principal.IsInRole(oppositeRole));
        Assert.True(principal.IsInRole(HireGameDevRoleNames.Admin));
        Assert.Equal(primaryRole, principal.FindFirstValue(HireGameDevClaimTypes.AccountType));
        // Legacy data is preserved, not silently deleted during authentication.
        Assert.True(await users.IsInRoleAsync(user, oppositeRole));
    }
}
