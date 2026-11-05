using Microsoft.AspNetCore.Authentication.Cookies;

namespace HireGameDev.Web.Portals;

/// <summary>
/// Stamps CookieOptions.Domain with the configured parent domain (e.g. ".hiregamedev.com")
/// ONLY when the incoming request host matches that domain tree, while leaving localhost
/// and health-check IP requests safely host-scoped.
/// </summary>
public sealed class PortalCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager _inner = new();
    private readonly string? _configuredSharedDomain;

    public PortalCookieManager(string? configuredSharedDomain)
    {
        _configuredSharedDomain = configuredSharedDomain;
    }

    public string? GetRequestCookie(HttpContext context, string key)
    {
        return _inner.GetRequestCookie(context, key);
    }

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options)
    {
        ApplyDomainIfEligible(context, options);
        _inner.AppendResponseCookie(context, key, value, options);
    }

    public void DeleteCookie(HttpContext context, string key, CookieOptions options)
    {
        ApplyDomainIfEligible(context, options);
        _inner.DeleteCookie(context, key, options);
    }

    private void ApplyDomainIfEligible(HttpContext context, CookieOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Domain))
        {
            return;
        }

        var resolvedDomain = PortalNavigator.ResolveCookieDomainStatic(
            context.Request.Host.Host,
            _configuredSharedDomain);

        if (!string.IsNullOrWhiteSpace(resolvedDomain))
        {
            options.Domain = resolvedDomain;
        }
    }
}
