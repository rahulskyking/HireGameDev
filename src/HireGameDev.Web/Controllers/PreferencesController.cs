using System.Security.Claims;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Auth;
using HireGameDev.Application.Abstractions.Consent;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Features.Settings;
using HireGameDev.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class PreferencesController : Controller
{
    public const string ThemeCookieName = "hgd-theme";
    public const string CookieConsentCookieName = "hgd-cookies";

    private readonly IAuthService _authService;
    private readonly IConsentService _consentService;
    private readonly ISettingsService _settingsService;
    private readonly IClock _clock;
    private readonly IWebHostEnvironment _environment;

    public PreferencesController(
        IAuthService authService,
        IConsentService consentService,
        ISettingsService settingsService,
        IClock clock,
        IWebHostEnvironment environment)
    {
        _authService = authService;
        _consentService = consentService;
        _settingsService = settingsService;
        _clock = clock;
        _environment = environment;
    }

    [HttpPost]
    public async Task<IActionResult> Theme(
        string? theme = null,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var (normalizedValue, enumValue) = ParseTheme(theme);

        Response.Cookies.Append(
            ThemeCookieName,
            normalizedValue,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = !_environment.IsDevelopment(),
                IsEssential = true,
                Expires = _clock.UtcNow.AddDays(365),
                Path = "/"
            });

        if (User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await _authService.UpdateThemePreferenceAsync(userId, enumValue, cancellationToken);
        }

        if (IsAjaxRequest())
        {
            return NoContent();
        }

        return RedirectToSafeLocalUrl(returnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Cookies(
        string? choice = null,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedChoice = string.Equals(choice, "accepted", StringComparison.OrdinalIgnoreCase)
            ? "accepted"
            : "essential";

        Response.Cookies.Append(
            CookieConsentCookieName,
            normalizedChoice,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = !_environment.IsDevelopment(),
                IsEssential = true,
                Expires = _clock.UtcNow.AddDays(180),
                Path = "/"
            });

        if (normalizedChoice == "accepted" &&
            User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var termsVersion = await _settingsService.GetStringAsync(
                AppSettingKeys.LegalTermsVersion,
                AppSettingKeys.DefaultLegalVersion,
                cancellationToken);

            await _consentService.RecordConsentAsync(
                userId,
                ConsentType.Cookies,
                termsVersion,
                cancellationToken: cancellationToken);
        }

        if (IsAjaxRequest())
        {
            return NoContent();
        }

        return RedirectToSafeLocalUrl(returnUrl);
    }

    private bool IsAjaxRequest()
        => string.Equals(
            Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

    private IActionResult RedirectToSafeLocalUrl(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    private static (string Normalized, ThemePreference EnumValue) ParseTheme(string? raw)
    {
        if (string.Equals(raw, "light", StringComparison.OrdinalIgnoreCase))
        {
            return ("light", ThemePreference.Light);
        }

        if (string.Equals(raw, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return ("dark", ThemePreference.Dark);
        }

        return ("system", ThemePreference.System);
    }
}
