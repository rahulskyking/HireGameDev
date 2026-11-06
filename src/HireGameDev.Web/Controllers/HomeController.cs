using System.Diagnostics;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Application.Features.Settings;
using HireGameDev.Web.Models;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController : Controller
{
    private readonly IReferenceDataService _referenceDataService;
    private readonly ISettingsService _settingsService;
    private readonly IPortalNavigator _portalNavigator;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IReferenceDataService referenceDataService,
        ISettingsService settingsService,
        IPortalNavigator portalNavigator,
        ILogger<HomeController> logger)
    {
        _referenceDataService = referenceDataService;
        _settingsService = settingsService;
        _portalNavigator = portalNavigator;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var summary = await LoadPortalSummaryAndUrlsAsync(cancellationToken);
        var portal = _portalNavigator.ResolvePortal(HttpContext);

        return portal switch
        {
            PortalSite.Jobs => View("JobsPortal", summary),
            PortalSite.Studio => View("StudioPortal", summary),
            _ => View("Index", summary)
        };
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs(CancellationToken cancellationToken)
    {
        if (_portalNavigator.IsSubdomainRoutingActive(HttpContext) &&
            _portalNavigator.ResolvePortal(HttpContext) != PortalSite.Jobs &&
            _portalNavigator.DeploymentMode == PortalDeploymentMode.All)
        {
            return Redirect(_portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/"));
        }

        var summary = await LoadPortalSummaryAndUrlsAsync(cancellationToken);
        return View("JobsPortal", summary);
    }

    [HttpGet("for-studios")]
    public async Task<IActionResult> ForStudios(CancellationToken cancellationToken)
    {
        if (_portalNavigator.IsSubdomainRoutingActive(HttpContext) &&
            _portalNavigator.ResolvePortal(HttpContext) != PortalSite.Studio &&
            _portalNavigator.DeploymentMode == PortalDeploymentMode.All)
        {
            return Redirect(_portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/"));
        }

        var summary = await LoadPortalSummaryAndUrlsAsync(cancellationToken);
        return View("StudioPortal", summary);
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

    private async Task<ReferenceDataSummary?> LoadPortalSummaryAndUrlsAsync(CancellationToken cancellationToken)
    {
        ReferenceDataSummary? summary = null;
        try
        {
            summary = await _referenceDataService.GetSummaryAsync(cancellationToken);
            var settings = await _settingsService.GetAllAsync(cancellationToken);
            var termsVersion = await _settingsService.GetStringAsync(
                AppSettingKeys.LegalTermsVersion,
                AppSettingKeys.DefaultLegalVersion,
                cancellationToken);
            var privacyVersion = await _settingsService.GetStringAsync(
                AppSettingKeys.LegalPrivacyVersion,
                AppSettingKeys.DefaultLegalVersion,
                cancellationToken);

            ViewData["AppSettingsCount"] = settings.Count;
            ViewData["LegalVersions"] = $"Terms v{termsVersion} / Privacy v{privacyVersion}";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dev foundation summary unavailable on Home.");
        }

        var subdomainActive = _portalNavigator.IsSubdomainRoutingActive(HttpContext);

        ViewData["LandingUrl"] = _portalNavigator.BuildPortalUrl(PortalSite.Landing, HttpContext, "/");
        ViewData["JobsUrl"] = _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/");
        ViewData["JobsRegisterUrl"] = subdomainActive
            ? _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/Account/Register")
            : "/Account/Register";
        ViewData["StudioUrl"] = _portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/");
        ViewData["StudioRegisterUrl"] = subdomainActive
            ? _portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/Account/Register")
            : "/Account/Register?type=studio";

        return summary;
    }
}
