using System.Security.Claims;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.Identity;

/// <summary>
/// Embeds lightweight HireGameDev claims (`account_type`, `display_name`, `theme`)
/// into the authentication cookie so policies and layout read them with zero extra DB queries.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        var accountTypeText = SnakeCaseEnumValueConverter<AccountType>.ToSnakeCaseValue(user.AccountType);
        var themeText = SnakeCaseEnumValueConverter<ThemePreference>.ToSnakeCaseValue(user.ThemePreference);

        identity.AddClaim(new Claim(HireGameDevClaimTypes.AccountType, accountTypeText));
        identity.AddClaim(new Claim(HireGameDevClaimTypes.DisplayName, user.DisplayName));
        identity.AddClaim(new Claim(HireGameDevClaimTypes.ThemePreference, themeText));

        return identity;
    }
}
