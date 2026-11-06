namespace HireGameDev.Web.Portals;

public interface IPortalNavigator
{
    PortalDeploymentMode DeploymentMode { get; }

    PortalSite ResolvePortal(HttpContext httpContext);

    bool IsSubdomainRoutingActive(HttpContext httpContext);

    bool IsPortalServedByThisDeployment(PortalSite site);

    string GetPortalBaseUrl(PortalSite target, HttpContext httpContext);

    string BuildPortalUrl(PortalSite target, HttpContext httpContext, string pathAndQuery = "/");

    bool IsSafeReturnUrl(string? returnUrl, HttpContext httpContext);

    string? ResolveSharedCookieDomain(HttpContext httpContext);

    bool TryBuildWwwCanonicalRedirect(HttpContext httpContext, out string redirectUrl);
}
