using System.Diagnostics;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Application.Features.Settings;
using HireGameDev.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController : Controller
{
    private readonly IReferenceDataService _referenceDataService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IReferenceDataService referenceDataService,
        ISettingsService settingsService,
        ILogger<HomeController> logger)
    {
        _referenceDataService = referenceDataService;
        _settingsService = settingsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
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
            _logger.LogDebug(ex, "Dev foundation summary unavailable on Home/Index.");
        }

        return View(summary);
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
}
