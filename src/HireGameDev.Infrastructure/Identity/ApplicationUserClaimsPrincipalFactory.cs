using System.Security.Claims;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.Identity;

/// <summary>
/// Embeds lightweight HireGameDev claims (`account_type`, `display_name`, `theme`)
/// plus admin roles. Candidate/Studio access follows the immutable primary account type.
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

        // Ignore legacy cross-account roles without deleting profiles or memberships.
        foreach (var claim in identity.FindAll(ClaimTypes.Role).ToArray())
        {
            if ((claim.Value == HireGameDevRoleNames.Candidate || claim.Value == HireGameDevRoleNames.Studio) &&
                claim.Value != accountTypeText)
            {
                identity.RemoveClaim(claim);
            }
        }

        if (!identity.HasClaim(ClaimTypes.Role, accountTypeText))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, accountTypeText));
        }

        identity.AddClaim(new Claim(HireGameDevClaimTypes.AccountType, accountTypeText));
        identity.AddClaim(new Claim(HireGameDevClaimTypes.DisplayName, user.DisplayName));
        identity.AddClaim(new Claim(HireGameDevClaimTypes.ThemePreference, themeText));

        return identity;
    }
}
