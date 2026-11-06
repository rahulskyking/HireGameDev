using System.Net;
using Microsoft.Extensions.Options;

namespace HireGameDev.Web.Portals;

public sealed class PortalNavigator : IPortalNavigator
{
    private readonly PortalOptions _options;
    private readonly IWebHostEnvironment _environment;

    public PortalNavigator(
        IOptions<PortalOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public PortalDeploymentMode DeploymentMode => _options.DeploymentMode;

    public PortalSite ResolvePortal(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return _options.DeploymentMode switch
        {
            PortalDeploymentMode.Landing => PortalSite.Landing,
            PortalDeploymentMode.Jobs => PortalSite.Jobs,
            PortalDeploymentMode.Studio => PortalSite.Studio,
            _ => IsSubdomainRoutingActive(httpContext)
                ? ResolvePortalFromHost(httpContext.Request.Host.Host)
                : PortalSite.Landing
        };
    }

    public bool IsSubdomainRoutingActive(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var host = httpContext.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host) || IsLocalSingleHost(host))
        {
            return false;
        }

        if (Uri.TryCreate(_options.LandingBaseUrl, UriKind.Absolute, out var landingUri) &&
            !string.IsNullOrWhiteSpace(landingUri.Host))
        {
            var rootDomain = landingUri.Host.TrimStart('.');
            return string.Equals(host, rootDomain, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith($".{rootDomain}", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public bool IsPortalServedByThisDeployment(PortalSite site)
    {
        return _options.DeploymentMode switch
        {
            PortalDeploymentMode.All => true,
            PortalDeploymentMode.Landing => site == PortalSite.Landing,
            PortalDeploymentMode.Jobs => site == PortalSite.Jobs,
            PortalDeploymentMode.Studio => site == PortalSite.Studio,
            _ => true
        };
    }

    public string GetPortalBaseUrl(PortalSite target, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var requestHost = httpContext.Request.Host.Host;
        if (!IsSubdomainRoutingActive(httpContext) &&
            TryResolveLocalSingleHostBaseUrl(httpContext.Request.Scheme, requestHost, httpContext.Request.Host.Port, out var singleHostBaseUrl))
        {
            return singleHostBaseUrl;
        }

        var configured = target switch
        {
            PortalSite.Jobs => _options.JobsBaseUrl,
            PortalSite.Studio => _options.StudioBaseUrl,
            _ => _options.LandingBaseUrl
        };

        return configured.TrimEnd('/');
    }

    public string BuildPortalUrl(PortalSite target, HttpContext httpContext, string pathAndQuery = "/")
    {
        var baseUrl = GetPortalBaseUrl(target, httpContext);

        if (string.IsNullOrWhiteSpace(pathAndQuery) || pathAndQuery == "/")
        {
            if (!IsSubdomainRoutingActive(httpContext) && _options.DeploymentMode == PortalDeploymentMode.All)
            {
                return target switch
                {
                    PortalSite.Jobs => $"{baseUrl}/jobs",
                    PortalSite.Studio => $"{baseUrl}/for-studios",
                    _ => $"{baseUrl}/"
                };
            }

            return $"{baseUrl}/";
        }

        var normalizedPath = pathAndQuery.StartsWith('/') ? pathAndQuery : $"/{pathAndQuery}";
        return $"{baseUrl}{normalizedPath}";
    }

    public bool IsSafeReturnUrl(string? returnUrl, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return false;
        }

        foreach (var ch in returnUrl)
        {
            if (ch < 0x20)
            {
                return false;
            }
        }

        // 1. Strict relative path check (starts with single '/', not '//' or '/\').
        if (returnUrl.StartsWith('/') &&
            !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return true;
        }

        // 2. Allowlisted absolute cross-portal URL check.
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isDevHttp = _environment.IsDevelopment() &&
                        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isDevHttp)
        {
            return false;
        }

        foreach (var site in new[] { PortalSite.Landing, PortalSite.Jobs, PortalSite.Studio })
        {
            var portalBase = GetPortalBaseUrl(site, httpContext);
            if (Uri.TryCreate(portalBase, UriKind.Absolute, out var allowedUri) &&
                string.Equals(uri.Authority, allowedUri.Authority, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public string? ResolveSharedCookieDomain(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return ResolveCookieDomainStatic(httpContext.Request.Host.Host, _options.SharedCookieDomain);
    }

    public bool TryBuildWwwCanonicalRedirect(HttpContext httpContext, out string redirectUrl)
    {
        redirectUrl = string.Empty;
        if (!_options.RedirectWwwToApex)
        {
            return false;
        }

        var method = httpContext.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            return false;
        }

        var host = httpContext.Request.Host.Host;
        if (!host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pathAndQuery = $"{httpContext.Request.PathBase}{httpContext.Request.Path}{httpContext.Request.QueryString}";
        redirectUrl = BuildPortalUrl(PortalSite.Landing, httpContext, pathAndQuery);
        return true;
    }

    internal static string? ResolveCookieDomainStatic(string? requestHost, string? configuredSharedDomain)
    {
        if (string.IsNullOrWhiteSpace(requestHost) || string.IsNullOrWhiteSpace(configuredSharedDomain))
        {
            return null;
        }

        var bareDomain = configuredSharedDomain.Trim().TrimStart('.');
        if (string.IsNullOrWhiteSpace(bareDomain) ||
            IsLocalSingleHost(requestHost) ||
            IsLocalSingleHost(bareDomain))
        {
            return null;
        }

        if (string.Equals(requestHost, bareDomain, StringComparison.OrdinalIgnoreCase) ||
            requestHost.EndsWith($".{bareDomain}", StringComparison.OrdinalIgnoreCase))
        {
            return $".{bareDomain}";
        }

        return null;
    }

    internal static bool IsLocalSingleHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(host, out _) ||
               string.Equals(host, "lvh.me", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".lvh.me", StringComparison.OrdinalIgnoreCase);
    }

    private static PortalSite ResolvePortalFromHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return PortalSite.Landing;
        }

        if (host.StartsWith("jobs.", StringComparison.OrdinalIgnoreCase))
        {
            return PortalSite.Jobs;
        }

        if (host.StartsWith("studio.", StringComparison.OrdinalIgnoreCase))
        {
            return PortalSite.Studio;
        }

        return PortalSite.Landing;
    }

    private static bool TryResolveLocalSingleHostBaseUrl(
        string scheme,
        string? requestHost,
        int? port,
        out string baseUrl)
    {
        baseUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(requestHost))
        {
            return false;
        }

        // Always normalize *.localhost / lvh.me back to bare localhost in local single-host dev.
        var targetHost = requestHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(requestHost, "lvh.me", StringComparison.OrdinalIgnoreCase) ||
                         requestHost.EndsWith(".lvh.me", StringComparison.OrdinalIgnoreCase)
            ? "localhost"
            : requestHost;

        var isDefaultPort = !port.HasValue ||
                            (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) && port == 80) ||
                            (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) && port == 443);

        baseUrl = isDefaultPort
            ? $"{scheme}://{targetHost}"
            : $"{scheme}://{targetHost}:{port.Value}";

        return true;
    }
}
