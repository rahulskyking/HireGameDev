namespace HireGameDev.Web.Portals;

public enum PortalSite
{
    Landing = 0,
    Jobs = 1,
    Studio = 2
}

public enum PortalDeploymentMode
{
    /// <summary>
    /// Default modular monolith mode: one HireGameDev.Web deployment serves
    /// hiregamedev.com, jobs.hiregamedev.com, and studio.hiregamedev.com via host routing.
    /// </summary>
    All = 0,

    /// <summary>
    /// Isolates this deployment instance strictly to hiregamedev.com (Landing, Legal, Admin).
    /// </summary>
    Landing = 1,

    /// <summary>
    /// Isolates this deployment instance strictly to jobs.hiregamedev.com (Job Board, Public Studio Profiles, Candidate App).
    /// </summary>
    Jobs = 2,

    /// <summary>
    /// Isolates this deployment instance strictly to studio.hiregamedev.com (Studio Workspace & ATS).
    /// </summary>
    Studio = 3
}

public sealed class PortalOptions
{
    public const string SectionName = "Portals";

    public PortalDeploymentMode DeploymentMode { get; set; } = PortalDeploymentMode.All;

    public string LandingBaseUrl { get; set; } = "https://hiregamedev.com";

    public string JobsBaseUrl { get; set; } = "https://jobs.hiregamedev.com";

    public string StudioBaseUrl { get; set; } = "https://studio.hiregamedev.com";

    /// <summary>
    /// Optional shared parent cookie domain (e.g. ".hiregamedev.com" in Production, ".lvh.me" in optional cross-subdomain dev).
    /// Left empty on localhost because RFC 6265 forbids Domain=.localhost.
    /// </summary>
    public string SharedCookieDomain { get; set; } = string.Empty;

    public bool RedirectWwwToApex { get; set; } = true;

    public string DataProtectionApplicationName { get; set; } = "HireGameDev";

    /// <summary>
    /// Optional shared filesystem directory (e.g. "/var/lib/hiregamedev/keys") so key rings survive restarts
    /// and can be shared across separate portal deployments.
    /// </summary>
    public string DataProtectionKeysPath { get; set; } = string.Empty;
}
