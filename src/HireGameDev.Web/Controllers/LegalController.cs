using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Features.Settings;
using HireGameDev.Web.Models.Legal;
using HireGameDev.Web.Portals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
[Route("Legal")]
public sealed class LegalController : Controller
{
    private readonly ISettingsService _settingsService;
    private readonly IPortalNavigator _portalNavigator;

    public LegalController(
        ISettingsService settingsService,
        IPortalNavigator portalNavigator)
    {
        _settingsService = settingsService;
        _portalNavigator = portalNavigator;
    }

    [HttpGet("Terms")]
    public async Task<IActionResult> Terms(CancellationToken cancellationToken)
    {
        var version = await _settingsService.GetStringAsync(
            AppSettingKeys.LegalTermsVersion,
            AppSettingKeys.DefaultLegalVersion,
            cancellationToken);

        return View(new LegalDocumentViewModel
        {
            Version = version,
            CanonicalUrl = _portalNavigator.BuildPortalUrl(PortalSite.Landing, HttpContext, "/Legal/Terms")
        });
    }

    [HttpGet("Privacy")]
    public async Task<IActionResult> Privacy(CancellationToken cancellationToken)
    {
        var version = await _settingsService.GetStringAsync(
            AppSettingKeys.LegalPrivacyVersion,
            AppSettingKeys.DefaultLegalVersion,
            cancellationToken);

        return View(new LegalDocumentViewModel
        {
            Version = version,
            CanonicalUrl = _portalNavigator.BuildPortalUrl(PortalSite.Landing, HttpContext, "/Legal/Privacy")
        });
    }

    [HttpGet("Grievance")]
    public async Task<IActionResult> Grievance(CancellationToken cancellationToken)
    {
        var officerName = await _settingsService.GetStringAsync(
            AppSettingKeys.LegalGrievanceOfficerName,
            "Grievance Officer (HireGameDev India)",
            cancellationToken);
        var officerEmail = await _settingsService.GetStringAsync(
            AppSettingKeys.LegalGrievanceOfficerEmail,
            "grievance@hiregamedev.com",
            cancellationToken);
        var slaDays = await _settingsService.GetIntAsync(
            AppSettingKeys.LegalGrievanceResponseDays,
            30,
            cancellationToken);

        return View(new GrievanceViewModel
        {
            OfficerName = officerName,
            OfficerEmail = officerEmail,
            ResponseSlaDays = Math.Clamp(slaDays, 1, 90),
            CanonicalUrl = _portalNavigator.BuildPortalUrl(PortalSite.Landing, HttpContext, "/Legal/Grievance")
        });
    }
}
