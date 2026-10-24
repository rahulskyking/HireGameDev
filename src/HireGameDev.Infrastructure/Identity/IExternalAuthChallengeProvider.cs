using Microsoft.AspNetCore.Authentication;

namespace HireGameDev.Infrastructure.Identity;

public interface IExternalAuthChallengeProvider
{
    AuthenticationProperties CreateChallengeProperties(string provider, string redirectUrl);
}
