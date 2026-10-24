using HireGameDev.Application.Abstractions.Auth;
using HireGameDev.Application.Features.Auth;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IExternalAuthChallengeProvider _externalChallengeProvider;

    public AccountController(
        IAuthService authService,
        IExternalAuthChallengeProvider externalChallengeProvider)
    {
        _authService = authService;
        _externalChallengeProvider = externalChallengeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Register(
        string? type = null,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToSafeLocalUrl(returnUrl);
        }

        var selectedType = string.Equals(type, "studio", StringComparison.OrdinalIgnoreCase)
            ? AccountType.Studio
            : AccountType.Candidate;

        return View(new RegisterViewModel
        {
            AccountType = selectedType,
            ReturnUrl = returnUrl,
            ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);
            return View(model);
        }

        var confirmBaseUrl = Url.Action(
            action: nameof(ConfirmEmail),
            controller: "Account",
            values: null,
            protocol: Request.Scheme) ?? string.Empty;

        var result = await _authService.RegisterAsync(
            new RegisterCommand(
                AccountType: model.AccountType,
                DisplayName: model.DisplayName,
                Email: model.Email,
                Password: model.Password,
                ConfirmAge18Plus: model.ConfirmAge18Plus,
                AcceptTermsAndPrivacy: model.AcceptTermsAndPrivacy,
                ConfirmationLinkBaseUrl: confirmBaseUrl),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            model.ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);
            return View(model);
        }

        return RedirectToAction(nameof(RegisterConfirmation), new { email = result.Value.Email });
    }

    [HttpGet]
    public IActionResult RegisterConfirmation(string email)
    {
        ViewData["Email"] = email;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(Guid userId, string code, CancellationToken cancellationToken)
    {
        var result = await _authService.ConfirmEmailAsync(
            new ConfirmEmailCommand(userId, code),
            cancellationToken);

        return View(new ConfirmEmailViewModel
        {
            Succeeded = result.IsSuccess,
            Message = result.IsSuccess
                ? "Your email address is verified. You can now sign in to your account."
                : result.Error.Message
        });
    }

    [HttpPost]
    public async Task<IActionResult> ResendConfirmation(string email, CancellationToken cancellationToken)
    {
        var confirmBaseUrl = Url.Action(
            action: nameof(ConfirmEmail),
            controller: "Account",
            values: null,
            protocol: Request.Scheme) ?? string.Empty;

        await _authService.ResendConfirmationAsync(
            new ResendConfirmationCommand(email, confirmBaseUrl),
            cancellationToken);

        TempData["StatusMessage"] = "If an unconfirmed account matches that email, a fresh confirmation link has been sent.";
        return RedirectToAction(nameof(RegisterConfirmation), new { email });
    }

    [HttpGet]
    public async Task<IActionResult> Login(
        string? returnUrl = null,
        string? remoteError = null,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToSafeLocalUrl(returnUrl);
        }

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            ModelState.AddModelError(string.Empty, remoteError);
        }

        if (TempData["ErrorMessage"] is string errorMessage && !string.IsNullOrWhiteSpace(errorMessage))
        {
            ModelState.AddModelError(string.Empty, errorMessage);
        }

        return View(new LoginViewModel
        {
            ReturnUrl = returnUrl,
            ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);
            return View(model);
        }

        var result = await _authService.LoginAsync(
            new LoginCommand(model.Email, model.Password, model.RememberMe),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            model.ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);
            return View(model);
        }

        var outcome = result.Value;
        switch (outcome.Status)
        {
            case LoginStatus.Succeeded:
                return RedirectToSafeLocalUrl(model.ReturnUrl);

            case LoginStatus.EmailNotConfirmed:
                model.ShowResendConfirmation = true;
                model.ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);
                ModelState.AddModelError(string.Empty, outcome.Message ?? "Please confirm your email address first.");
                return View(model);

            case LoginStatus.PasswordNotSetForSocialAccount:
                model.ShowSetPasswordPrompt = true;
                model.ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);
                ModelState.AddModelError(string.Empty, outcome.Message ?? "No password is set on this social account yet.");
                return View(model);

            default:
                model.ExternalProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);
                ModelState.AddModelError(string.Empty, outcome.Message ?? "Invalid email address or password.");
                return View(model);
        }
    }

    [HttpPost]
    public async Task<IActionResult> ExternalLogin(
        string provider,
        AccountType? preferredAccountType = null,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var validation = await _authService.ValidateExternalChallengeAsync(
            provider,
            preferredAccountType,
            cancellationToken);

        if (validation.IsFailure)
        {
            TempData["ErrorMessage"] = validation.Error.Message;
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var callbackUrl = Url.Action(
            action: nameof(ExternalLoginCallback),
            controller: "Account",
            values: new { returnUrl, preferredAccountType }) ?? "/";

        var properties = _externalChallengeProvider.CreateChallengeProperties(provider, callbackUrl);
        return Challenge(properties, provider);
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(
        string? returnUrl = null,
        AccountType? preferredAccountType = null,
        string? remoteError = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _authService.HandleExternalLoginCallbackAsync(remoteError, cancellationToken);

        switch (outcome.Status)
        {
            case ExternalCallbackStatus.SignedIn:
                return RedirectToSafeLocalUrl(returnUrl);

            case ExternalCallbackStatus.RequiresAccountCompletion:
                return RedirectToAction(
                    nameof(CompleteExternalRegistration),
                    new { preferredAccountType, returnUrl });

            default:
                TempData["ErrorMessage"] = outcome.Message ?? "Unable to complete external sign-in.";
                return RedirectToAction(nameof(Login), new { returnUrl });
        }
    }

    [HttpGet]
    public async Task<IActionResult> CompleteExternalRegistration(
        AccountType? preferredAccountType = null,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToSafeLocalUrl(returnUrl);
        }

        var pendingResult = await _authService.GetPendingExternalRegistrationAsync(cancellationToken);
        if (pendingResult.IsFailure)
        {
            TempData["ErrorMessage"] = pendingResult.Error.Message;
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var pending = pendingResult.Value;
        var selectedType = preferredAccountType == AccountType.Studio && pending.AllowedForStudios
            ? AccountType.Studio
            : AccountType.Candidate;

        return View(new CompleteExternalRegistrationViewModel
        {
            ProviderDisplayName = pending.ProviderDisplayName,
            Email = pending.Email,
            AllowedForStudios = pending.AllowedForStudios,
            AccountType = selectedType,
            DisplayName = pending.SuggestedDisplayName,
            ReturnUrl = returnUrl
        });
    }

    [HttpPost]
    public async Task<IActionResult> CompleteExternalRegistration(
        CompleteExternalRegistrationViewModel model,
        CancellationToken cancellationToken = default)
    {
        var pendingResult = await _authService.GetPendingExternalRegistrationAsync(cancellationToken);
        if (pendingResult.IsFailure)
        {
            TempData["ErrorMessage"] = pendingResult.Error.Message;
            return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl });
        }

        var pending = pendingResult.Value;
        model.ProviderDisplayName = pending.ProviderDisplayName;
        model.Email = pending.Email;
        model.AllowedForStudios = pending.AllowedForStudios;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var setPasswordBaseUrl = Url.Action(
            action: nameof(ResetPassword),
            controller: "Account",
            values: null,
            protocol: Request.Scheme) ?? string.Empty;

        var result = await _authService.CompleteExternalRegistrationAsync(
            new CompleteExternalRegistrationCommand(
                AccountType: model.AccountType,
                DisplayName: model.DisplayName,
                ConfirmAge18Plus: model.ConfirmAge18Plus,
                AcceptTermsAndPrivacy: model.AcceptTermsAndPrivacy,
                SetPasswordLinkBaseUrl: setPasswordBaseUrl),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return View(model);
        }

        return RedirectToSafeLocalUrl(model.ReturnUrl);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(cancellationToken);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword(string? email = null)
    {
        return View(new ForgotPasswordViewModel
        {
            Email = email ?? string.Empty
        });
    }

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var resetBaseUrl = Url.Action(
            action: nameof(ResetPassword),
            controller: "Account",
            values: null,
            protocol: Request.Scheme) ?? string.Empty;

        var result = await _authService.SendPasswordResetOrSetupLinkAsync(
            new ForgotPasswordCommand(model.Email, resetBaseUrl),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return View(model);
        }

        model.LinkSent = true;
        return View(model);
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email = null, string? code = null, string? mode = "reset")
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return RedirectToAction(nameof(ForgotPassword));
        }

        return View(new ResetPasswordViewModel
        {
            Email = email ?? string.Empty,
            Code = code,
            Mode = string.Equals(mode, "set", StringComparison.OrdinalIgnoreCase) ? "set" : "reset"
        });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _authService.ResetPasswordAsync(
            new ResetPasswordCommand(model.Email, model.Code, model.NewPassword),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            return View(model);
        }

        TempData["StatusMessage"] = model.Mode == "set"
            ? "Your password has been set. You can now sign in with your email and password."
            : "Your password has been updated. Please sign in with your new password.";

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private IActionResult RedirectToSafeLocalUrl(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }
}
