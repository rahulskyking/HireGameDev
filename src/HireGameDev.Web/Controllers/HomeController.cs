using System.Diagnostics;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class HomeController : Controller
{
    private readonly IReferenceDataService _referenceDataService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IReferenceDataService referenceDataService,
        ILogger<HomeController> logger)
    {
        _referenceDataService = referenceDataService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ReferenceDataSummary? summary = null;
        try
        {
            summary = await _referenceDataService.GetSummaryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reference data summary unavailable on Home/Index.");
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
