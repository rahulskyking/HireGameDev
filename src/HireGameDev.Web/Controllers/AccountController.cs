using System.Security.Claims;
using HireGameDev.Application.Abstractions.Auth;
using HireGameDev.Application.Features.Auth;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Web.Models.Account;
using HireGameDev.Web.Portals;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HireGameDev.Web.Controllers;

[AllowAnonymous]
public sealed class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IExternalAuthChallengeProvider _externalChallengeProvider;
    private readonly IPortalNavigator _portalNavigator;

    public AccountController(
        IAuthService authService,
        IExternalAuthChallengeProvider externalChallengeProvider,
        IPortalNavigator portalNavigator)
    {
        _authService = authService;
        _externalChallengeProvider = externalChallengeProvider;
        _portalNavigator = portalNavigator;
    }

    [HttpGet]
    public async Task<IActionResult> Register(
        string? type = null,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var portal = _portalNavigator.ResolvePortal(HttpContext);

        // ADR-012 (A1): Route registration from apex landing domain directly to the dedicated Candidate or Studio portal when subdomain routing is active.
        if (portal == PortalSite.Landing &&
            _portalNavigator.DeploymentMode == PortalDeploymentMode.All &&
            _portalNavigator.IsSubdomainRoutingActive(HttpContext))
        {
            var targetPortal = string.Equals(type, "studio", StringComparison.OrdinalIgnoreCase)
                ? PortalSite.Studio
                : PortalSite.Jobs;

            var query = !string.IsNullOrWhiteSpace(returnUrl) && _portalNavigator.IsSafeReturnUrl(returnUrl, HttpContext)
                ? $"/Account/Register?returnUrl={Uri.EscapeDataString(returnUrl)}"
                : "/Account/Register";

            return Redirect(_portalNavigator.BuildPortalUrl(targetPortal, HttpContext, query));
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            if (portal == PortalSite.Studio && !UserHasRole(HireGameDevRoleNames.Studio) && !UserHasRole(HireGameDevRoleNames.Admin))
            {
                return Redirect(_portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/Candidate/Profile"));
            }

            if (portal == PortalSite.Jobs && !UserHasRole(HireGameDevRoleNames.Candidate) && !UserHasRole(HireGameDevRoleNames.Admin))
            {
                return Redirect(_portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/Studio"));
            }

            return RedirectAfterAuth(returnUrl, GetCurrentUserRoles());
        }

        var selectedType = portal switch
        {
            PortalSite.Studio => AccountType.Studio,
            PortalSite.Jobs => AccountType.Candidate,
            _ => string.Equals(type, "studio", StringComparison.OrdinalIgnoreCase)
                ? AccountType.Studio
                : AccountType.Candidate
        };

        var viewModel = new RegisterViewModel
        {
            AccountType = selectedType,
            ReturnUrl = returnUrl
        };

        await PopulateRegisterPortalMetadataAsync(viewModel, portal, cancellationToken);
        return View(viewModel);
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AuthRegister)]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        var portal = _portalNavigator.ResolvePortal(HttpContext);
        if (portal == PortalSite.Jobs)
        {
            model.AccountType = AccountType.Candidate;
        }
        else if (portal == PortalSite.Studio)
        {
            model.AccountType = AccountType.Studio;
        }

        if (!ModelState.IsValid)
        {
            await PopulateRegisterPortalMetadataAsync(model, portal, cancellationToken);
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
            await PopulateRegisterPortalMetadataAsync(model, portal, cancellationToken);
            return View(model);
        }

        return RedirectToAction(nameof(RegisterConfirmation), new { email = result.Value.Email });
    }

    [HttpGet]
    public IActionResult RegisterConfirmation(string? email = null)
    {
        ViewData["Email"] = email ?? string.Empty;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(Guid? userId = null, string? code = null, CancellationToken cancellationToken = default)
    {
        if (userId is null || userId == Guid.Empty || string.IsNullOrWhiteSpace(code))
        {
            return View(new ConfirmEmailViewModel
            {
                Succeeded = false,
                Message = "Invalid or incomplete email verification link."
            });
        }

        var result = await _authService.ConfirmEmailAsync(
            new ConfirmEmailCommand(userId.Value, code),
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
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
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
        var portal = _portalNavigator.ResolvePortal(HttpContext);

        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectAfterAuth(returnUrl, GetCurrentUserRoles());
        }

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            ModelState.AddModelError(string.Empty, remoteError);
        }

        if (TempData["ErrorMessage"] is string errorMessage && !string.IsNullOrWhiteSpace(errorMessage))
        {
            ModelState.AddModelError(string.Empty, errorMessage);
        }

        var viewModel = new LoginViewModel
        {
            ReturnUrl = returnUrl
        };

        await PopulateLoginPortalMetadataAsync(viewModel, portal, cancellationToken);
        return View(viewModel);
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        var portal = _portalNavigator.ResolvePortal(HttpContext);

        if (!ModelState.IsValid)
        {
            await PopulateLoginPortalMetadataAsync(model, portal, cancellationToken);
            return View(model);
        }

        var result = await _authService.LoginAsync(
            new LoginCommand(model.Email, model.Password, model.RememberMe),
            cancellationToken);

        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error.Message);
            await PopulateLoginPortalMetadataAsync(model, portal, cancellationToken);
            return View(model);
        }

        var outcome = result.Value;
        switch (outcome.Status)
        {
            case LoginStatus.Succeeded:
                return RedirectAfterAuth(model.ReturnUrl, outcome.Roles ?? []);

            case LoginStatus.EmailNotConfirmed:
                model.ShowResendConfirmation = true;
                await PopulateLoginPortalMetadataAsync(model, portal, cancellationToken);
                ModelState.AddModelError(string.Empty, outcome.Message ?? "Please confirm your email address first.");
                return View(model);

            case LoginStatus.PasswordNotSetForSocialAccount:
                model.ShowSetPasswordPrompt = true;
                await PopulateLoginPortalMetadataAsync(model, portal, cancellationToken);
                ModelState.AddModelError(string.Empty, outcome.Message ?? "No password is set on this social account yet.");
                return View(model);

            default:
                await PopulateLoginPortalMetadataAsync(model, portal, cancellationToken);
                ModelState.AddModelError(string.Empty, outcome.Message ?? "Invalid email address or password.");
                return View(model);
        }
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AuthRegister)]
    public async Task<IActionResult> ExternalLogin(
        string provider,
        AccountType? preferredAccountType = null,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var portal = _portalNavigator.ResolvePortal(HttpContext);
        var effectiveAccountType = portal switch
        {
            PortalSite.Studio => AccountType.Studio,
            PortalSite.Jobs => AccountType.Candidate,
            _ => preferredAccountType
        };

        var validation = await _authService.ValidateExternalChallengeAsync(
            provider,
            effectiveAccountType,
            cancellationToken);

        if (validation.IsFailure)
        {
            TempData["ErrorMessage"] = validation.Error.Message;
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var callbackUrl = Url.Action(
            action: nameof(ExternalLoginCallback),
            controller: "Account",
            values: new { returnUrl, preferredAccountType = effectiveAccountType }) ?? "/";

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
        var portal = _portalNavigator.ResolvePortal(HttpContext);
        AccountType? targetPortalRole = portal switch
        {
            PortalSite.Studio => AccountType.Studio,
            PortalSite.Jobs => AccountType.Candidate,
            _ => preferredAccountType
        };

        var outcome = await _authService.HandleExternalLoginCallbackAsync(
            remoteError,
            targetPortalRole,
            cancellationToken);

        switch (outcome.Status)
        {
            case ExternalCallbackStatus.SignedIn:
                return RedirectAfterAuth(returnUrl, outcome.Roles ?? []);

            case ExternalCallbackStatus.RequiresAccountCompletion:
                return RedirectToAction(
                    nameof(CompleteExternalRegistration),
                    new { preferredAccountType = targetPortalRole, returnUrl });

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
            return RedirectAfterAuth(returnUrl, GetCurrentUserRoles());
        }

        var portal = _portalNavigator.ResolvePortal(HttpContext);
        var pendingResult = await _authService.GetPendingExternalRegistrationAsync(cancellationToken);
        if (pendingResult.IsFailure)
        {
            TempData["ErrorMessage"] = pendingResult.Error.Message;
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var pending = pendingResult.Value;
        var selectedType = portal switch
        {
            PortalSite.Studio when pending.AllowedForStudios => AccountType.Studio,
            PortalSite.Jobs => AccountType.Candidate,
            _ => preferredAccountType == AccountType.Studio && pending.AllowedForStudios
                ? AccountType.Studio
                : AccountType.Candidate
        };

        return View(new CompleteExternalRegistrationViewModel
        {
            Portal = portal,
            IsPortalRoleLocked = portal is PortalSite.Jobs or PortalSite.Studio,
            ProviderDisplayName = pending.ProviderDisplayName,
            Email = pending.Email,
            AllowedForStudios = pending.AllowedForStudios,
            AccountType = selectedType,
            DisplayName = pending.SuggestedDisplayName,
            ReturnUrl = returnUrl
        });
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AuthRegister)]
    public async Task<IActionResult> CompleteExternalRegistration(
        CompleteExternalRegistrationViewModel model,
        CancellationToken cancellationToken = default)
    {
        var portal = _portalNavigator.ResolvePortal(HttpContext);
        var pendingResult = await _authService.GetPendingExternalRegistrationAsync(cancellationToken);
        if (pendingResult.IsFailure)
        {
            TempData["ErrorMessage"] = pendingResult.Error.Message;
            return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl });
        }

        var pending = pendingResult.Value;
        model.Portal = portal;
        model.IsPortalRoleLocked = portal is PortalSite.Jobs or PortalSite.Studio;
        model.ProviderDisplayName = pending.ProviderDisplayName;
        model.Email = pending.Email;
        model.AllowedForStudios = pending.AllowedForStudios;

        if (portal == PortalSite.Jobs)
        {
            model.AccountType = AccountType.Candidate;
        }
        else if (portal == PortalSite.Studio && pending.AllowedForStudios)
        {
            model.AccountType = AccountType.Studio;
        }

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

        return RedirectAfterAuth(model.ReturnUrl, result.Value.Roles ?? []);
    }

    [Authorize]
    [HttpGet]
    public IActionResult EnableRole(AccountType role = AccountType.Studio, string? returnUrl = null)
    {
        TempData["ErrorMessage"] = "Candidate and Studio accounts are separate. Register the other account type with a different email.";
        return RedirectToAction(nameof(Manage));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> EnableRole(
        AccountType targetRole,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _authService.EnablePortalRoleAsync(userId, targetRole, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
            return RedirectToAction(nameof(Manage));
        }

        var roleLabel = targetRole == AccountType.Studio ? "Studio" : "Candidate";
        TempData["StatusMessage"] = $"{roleLabel} workspace enabled on your account.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && _portalNavigator.IsSafeReturnUrl(returnUrl, HttpContext))
        {
            return Redirect(returnUrl);
        }

        var destinationPortal = targetRole == AccountType.Studio ? PortalSite.Studio : PortalSite.Jobs;
        var destinationPath = targetRole == AccountType.Studio ? "/Studio" : "/Candidate/Profile";
        return Redirect(_portalNavigator.BuildPortalUrl(destinationPortal, HttpContext, destinationPath));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Manage(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var stateResult = await _authService.GetAccountManageStateAsync(userId, cancellationToken);
        if (stateResult.IsFailure)
        {
            await _authService.LogoutAsync(cancellationToken);
            return RedirectToAction(nameof(Login));
        }

        return View(BuildManageViewModel(stateResult.Value));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> UpdateProfile(
        [Bind(Prefix = "ProfileInput")] UpdateProfileInputModel input,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _authService.UpdateDisplayNameAsync(
            new UpdateDisplayNameCommand(userId, input.DisplayName),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Display name updated.";
        }

        return RedirectToAction(nameof(Manage));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> ChangePassword(
        [Bind(Prefix = "PasswordInput")] ChangePasswordInputModel input,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            var stateResult = await _authService.GetAccountManageStateAsync(userId, cancellationToken);
            if (stateResult.IsFailure)
            {
                return RedirectToAction(nameof(Login));
            }

            return View("Manage", BuildManageViewModel(stateResult.Value, passwordInput: input));
        }

        var result = await _authService.ChangePasswordAsync(
            new ChangePasswordCommand(userId, input.CurrentPassword, input.NewPassword),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Your password has been updated.";
        }

        return RedirectToAction(nameof(Manage));
    }

    [Authorize]
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
    public async Task<IActionResult> SendSetPasswordLink(CancellationToken cancellationToken)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToAction(nameof(Manage));
        }

        var resetBaseUrl = Url.Action(
            action: nameof(ResetPassword),
            controller: "Account",
            values: null,
            protocol: Request.Scheme) ?? string.Empty;

        await _authService.SendPasswordResetOrSetupLinkAsync(
            new ForgotPasswordCommand(email, resetBaseUrl),
            cancellationToken);

        TempData["StatusMessage"] = $"Sent a secure Set-Password link to {email}.";
        return RedirectToAction(nameof(Manage));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> LinkExternal(
        string provider,
        string? currentPassword = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var authCheck = await _authService.AuthorizeExternalLinkAsync(
            userId,
            provider,
            currentPassword,
            cancellationToken);

        if (authCheck.IsFailure)
        {
            TempData["ErrorMessage"] = authCheck.Error.Message;
            return RedirectToAction(nameof(Manage));
        }

        var callbackUrl = Url.Action(nameof(LinkExternalCallback), "Account") ?? "/Account/Manage";
        var properties = _externalChallengeProvider.CreateChallengeProperties(
            provider,
            callbackUrl,
            userId.ToString());

        return Challenge(properties, provider);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> LinkExternalCallback(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _authService.CompleteExternalLinkAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Connected account linked successfully.";
        }

        return RedirectToAction(nameof(Manage));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> UnlinkExternal(
        string loginProvider,
        string providerKey,
        string? currentPassword = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _authService.UnlinkExternalLoginAsync(
            new UnlinkExternalLoginCommand(userId, loginProvider, providerKey, currentPassword),
            cancellationToken);

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = $"{loginProvider} has been unlinked from your account.";
        }

        return RedirectToAction(nameof(Manage));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AcceptLegal(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _authService.AcceptCurrentLegalConsentsAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["StatusMessage"] = "Recorded acceptance of current Terms of Use and Privacy Notice.";
        }

        return RedirectToAction(nameof(Manage));
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
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
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
    [EnableRateLimiting(RateLimitPolicies.AuthLogin)]
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

    private AccountManageViewModel BuildManageViewModel(
        AccountManageState state,
        ChangePasswordInputModel? passwordInput = null)
    {
        return new AccountManageViewModel
        {
            State = state,
            ProfileInput = new UpdateProfileInputModel { DisplayName = state.DisplayName },
            PasswordInput = passwordInput ?? new ChangePasswordInputModel(),
            JobsPortalUrl = _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/Candidate/Profile"),
            StudioPortalUrl = _portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/Studio")
        };
    }

    private async Task PopulateRegisterPortalMetadataAsync(
        RegisterViewModel model,
        PortalSite portal,
        CancellationToken cancellationToken)
    {
        var allProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);

        model.Portal = portal;
        model.IsPortalRoleLocked = portal is PortalSite.Jobs or PortalSite.Studio;
        model.ExternalProviders = portal == PortalSite.Studio
            ? allProviders.Where(p => p.AllowedForStudios).ToArray()
            : allProviders;
        model.AlternatePortalUrl = portal == PortalSite.Studio
            ? _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/Account/Register")
            : _portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/Account/Register");
    }

    private async Task PopulateLoginPortalMetadataAsync(
        LoginViewModel model,
        PortalSite portal,
        CancellationToken cancellationToken)
    {
        var allProviders = await _authService.GetConfiguredExternalProvidersAsync(cancellationToken);
        var subdomainActive = _portalNavigator.IsSubdomainRoutingActive(HttpContext);

        model.Portal = portal;
        model.ExternalProviders = portal == PortalSite.Studio
            ? allProviders.Where(p => p.AllowedForStudios).ToArray()
            : allProviders;
        model.AlternatePortalLoginUrl = portal == PortalSite.Studio
            ? _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/Account/Login")
            : _portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/Account/Login");
        model.RegisterUrl = portal == PortalSite.Landing && subdomainActive
            ? _portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/Account/Register")
            : "/Account/Register";
    }

    private IActionResult RedirectAfterAuth(string? returnUrl, IReadOnlyList<string> roles)
    {
        var portal = _portalNavigator.ResolvePortal(HttpContext);
        var hasAdmin = roles.Any(r => string.Equals(r, HireGameDevRoleNames.Admin, StringComparison.OrdinalIgnoreCase));
        var hasStudio = roles.Any(r => string.Equals(r, HireGameDevRoleNames.Studio, StringComparison.OrdinalIgnoreCase));
        var hasCandidate = roles.Any(r => string.Equals(r, HireGameDevRoleNames.Candidate, StringComparison.OrdinalIgnoreCase));

        // Wrong-portal sign-ins return to the account's own workspace.
        if (portal == PortalSite.Studio && !hasStudio && !hasAdmin)
        {
            return Redirect(_portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/Candidate/Profile"));
        }

        // Studio accounts never acquire Candidate access.
        if (portal == PortalSite.Jobs && !hasCandidate && !hasAdmin)
        {
            return Redirect(_portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/Studio"));
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && _portalNavigator.IsSafeReturnUrl(returnUrl, HttpContext))
        {
            return Redirect(returnUrl);
        }

        if (portal == PortalSite.Landing && _portalNavigator.IsSubdomainRoutingActive(HttpContext))
        {
            if (hasAdmin)
            {
                return Redirect("/Admin");
            }

            if (hasStudio && !hasCandidate)
            {
                return Redirect(_portalNavigator.BuildPortalUrl(PortalSite.Studio, HttpContext, "/Studio"));
            }

            if (hasCandidate)
            {
                return Redirect(_portalNavigator.BuildPortalUrl(PortalSite.Jobs, HttpContext, "/Candidate/Profile"));
            }
        }

        if (portal == PortalSite.Landing && !_portalNavigator.IsSubdomainRoutingActive(HttpContext))
        {
            if (hasAdmin)
            {
                return Redirect("/Admin");
            }

            if (hasStudio && !hasCandidate)
            {
                return Redirect("/Studio");
            }

            if (hasCandidate)
            {
                return Redirect("/Candidate/Profile");
            }
        }

        if (portal == PortalSite.Studio && hasStudio)
        {
            return Redirect("/Studio");
        }

        return RedirectToAction("Index", "Home");
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) &&
               userId != Guid.Empty;
    }

    private bool UserHasRole(string roleName)
    {
        return User.IsInRole(roleName) ||
               string.Equals(
                   User.FindFirstValue(HireGameDevClaimTypes.AccountType),
                   roleName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> GetCurrentUserRoles()
    {
        return User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
