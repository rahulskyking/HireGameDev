using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace HireGameDev.Infrastructure.Identity;

internal sealed class IdentityExternalAuthChallengeProvider : IExternalAuthChallengeProvider
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public IdentityExternalAuthChallengeProvider(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public AuthenticationProperties CreateChallengeProperties(string provider, string redirectUrl)
    {
        return _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
    }
}
