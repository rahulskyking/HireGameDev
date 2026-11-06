using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace HireGameDev.Web.Portals;

public static class PortalServiceCollectionExtensions
{
    public static IServiceCollection AddHireGameDevPortalsAndCookieSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var portalSection = configuration.GetSection(PortalOptions.SectionName);
        services.Configure<PortalOptions>(portalSection);

        var portalOptions = new PortalOptions();
        portalSection.Bind(portalOptions);

        services.AddSingleton<IPortalNavigator, PortalNavigator>();

        // 1. Shared Data Protection discriminator (+ optional shared key ring directory for multi-instance / split deployments).
        var appName = string.IsNullOrWhiteSpace(portalOptions.DataProtectionApplicationName)
            ? "HireGameDev"
            : portalOptions.DataProtectionApplicationName.Trim();

        var dataProtection = services.AddDataProtection()
            .SetApplicationName(appName);

        if (!string.IsNullOrWhiteSpace(portalOptions.DataProtectionKeysPath))
        {
            var directory = new DirectoryInfo(portalOptions.DataProtectionKeysPath.Trim());
            directory.Create();
            dataProtection.PersistKeysToFileSystem(directory);
        }

        // 2. Attach host-aware shared parent domain cookie manager (.hiregamedev.com in Prod) to Identity cookies.
        var cookieManager = new PortalCookieManager(portalOptions.SharedCookieDomain);

        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            options.CookieManager = cookieManager;
        });

        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options =>
        {
            options.CookieManager = cookieManager;
        });

        // 3. Harden CSRF anti-forgery cookie: host-only (no Domain attribute) + SameSite=Strict + HttpOnly.
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "hgd.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        return services;
    }

    public static IApplicationBuilder UseHireGameDevPortalRoutingGuard(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            // Health check always responds regardless of portal deployment slice.
            if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var navigator = context.RequestServices.GetRequiredService<IPortalNavigator>();

            if (navigator.TryBuildWwwCanonicalRedirect(context, out var canonicalUrl))
            {
                context.Response.Redirect(canonicalUrl, permanent: true);
                return;
            }

            // Normalize legacy *.localhost / lvh.me dev URLs straight back to bare localhost.
            var incomingHost = context.Request.Host.Host;
            if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
                (incomingHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(incomingHost, "lvh.me", StringComparison.OrdinalIgnoreCase) ||
                 incomingHost.EndsWith(".lvh.me", StringComparison.OrdinalIgnoreCase)))
            {
                var pathAndQuery = $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
                context.Response.Redirect(navigator.BuildPortalUrl(PortalSite.Landing, context, pathAndQuery), permanent: false);
                return;
            }

            // When deployed as a single-portal slice (e.g. DeploymentMode = Jobs), reject requests meant for other host prefixes.
            if (navigator.DeploymentMode != PortalDeploymentMode.All)
            {
                var host = context.Request.Host.Host;
                var hostImpliedPortal = host.StartsWith("jobs.", StringComparison.OrdinalIgnoreCase)
                    ? PortalSite.Jobs
                    : host.StartsWith("studio.", StringComparison.OrdinalIgnoreCase)
                        ? PortalSite.Studio
                        : PortalSite.Landing;

                if (!navigator.IsPortalServedByThisDeployment(hostImpliedPortal))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }

            await next();
        });
    }
}
