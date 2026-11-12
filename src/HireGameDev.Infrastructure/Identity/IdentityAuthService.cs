using System.Net;
using System.Security.Claims;
using System.Text;
using FluentValidation;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Auth;
using HireGameDev.Application.Abstractions.Consent;
using HireGameDev.Application.Abstractions.Email;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Auth;
using HireGameDev.Application.Features.Settings;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace HireGameDev.Infrastructure.Identity;

internal sealed class IdentityAuthService : IAuthService
{
    private const string EmailVerifiedClaimType = "email_verified";
    private const string GitHubProviderScheme = "GitHub";
    private static readonly TimeSpan SensitiveActionReauthWindow = TimeSpan.FromMinutes(10);

    private static readonly string[] CoreRoleNames =
    [
        HireGameDevRoleNames.Candidate,
        HireGameDevRoleNames.Studio,
        HireGameDevRoleNames.Admin
    ];

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IEmailSender _emailSender;
    private readonly IConsentService _consentService;
    private readonly ISettingsService _settingsService;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;
    private readonly ILogger<IdentityAuthService> _logger;
    private readonly IValidator<RegisterCommand> _registerValidator;
    private readonly IValidator<LoginCommand> _loginValidator;
    private readonly IValidator<ForgotPasswordCommand> _forgotPasswordValidator;
    private readonly IValidator<ResetPasswordCommand> _resetPasswordValidator;
    private readonly IValidator<CompleteExternalRegistrationCommand> _completeExternalValidator;
    private readonly IValidator<UpdateDisplayNameCommand> _updateDisplayNameValidator;
    private readonly IValidator<ChangePasswordCommand> _changePasswordValidator;
    private readonly IAdminBootstrapSeeder? _adminBootstrapSeeder;

    public IdentityAuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        IEmailSender emailSender,
        IConsentService consentService,
        ISettingsService settingsService,
        IAuditLogger auditLogger,
        IClock clock,
        ILogger<IdentityAuthService> logger,
        IValidator<RegisterCommand> registerValidator,
        IValidator<LoginCommand> loginValidator,
        IValidator<ForgotPasswordCommand> forgotPasswordValidator,
        IValidator<ResetPasswordCommand> resetPasswordValidator,
        IValidator<CompleteExternalRegistrationCommand> completeExternalValidator,
        IValidator<UpdateDisplayNameCommand> updateDisplayNameValidator,
        IValidator<ChangePasswordCommand> changePasswordValidator,
        IAdminBootstrapSeeder? adminBootstrapSeeder = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _emailSender = emailSender;
        _consentService = consentService;
        _settingsService = settingsService;
        _auditLogger = auditLogger;
        _clock = clock;
        _logger = logger;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _forgotPasswordValidator = forgotPasswordValidator;
        _resetPasswordValidator = resetPasswordValidator;
        _completeExternalValidator = completeExternalValidator;
        _updateDisplayNameValidator = updateDisplayNameValidator;
        _changePasswordValidator = changePasswordValidator;
        _adminBootstrapSeeder = adminBootstrapSeeder;
    }

    public async Task<Result<RegisterResult>> RegisterAsync(
        RegisterCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _registerValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<RegisterResult>(
                Error.Validation("Auth.ValidationFailed", validation.Errors[0].ErrorMessage));
        }

        var email = command.Email.Trim();
        var displayName = command.DisplayName.Trim();

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            return Result.Failure<RegisterResult>(
                Error.Conflict(
                    "Auth.EmailAlreadyExists",
                    "An account with this email address already exists. Sign in instead—one HireGameDev account can enable both Candidate and Studio workspaces."));
        }

        await EnsureCoreRolesExistAsync();

        var now = _clock.UtcNow;
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            AccountType = command.AccountType,
            IsActive = true,
            ThemePreference = ThemePreference.System,
            Timezone = "Asia/Kolkata",
            AgeConfirmedAt = now
        };

        var createResult = await _userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
        {
            var message = string.Join(" ", createResult.Errors.Select(e => e.Description));
            return Result.Failure<RegisterResult>(
                Error.Validation("Auth.RegistrationFailed", message));
        }

        var roleName = SnakeCaseEnumValueConverter<AccountType>.ToSnakeCaseValue(command.AccountType);
        await _userManager.AddToRoleAsync(user, roleName);

        await _consentService.RecordRegistrationConsentsAsync(user.Id, cancellationToken: cancellationToken);

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "auth.register",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id,
                AfterData: new { accountType = roleName, method = "email_password" }),
            cancellationToken);

        await SendConfirmationEmailInternalAsync(user, command.ConfirmationLinkBaseUrl, cancellationToken);

        return Result.Success(new RegisterResult(user.Id, email, ConfirmationEmailSent: true));
    }

    public async Task<Result> ConfirmEmailAsync(
        ConfirmEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.UserId == Guid.Empty || string.IsNullOrWhiteSpace(command.Code))
        {
            return Result.Failure(
                Error.Validation("Auth.InvalidConfirmationLink", "The email confirmation link is invalid."));
        }

        var user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            return Result.Failure(
                Error.NotFound("Auth.UserNotFound", "Unable to verify this confirmation link."));
        }

        if (user.EmailConfirmed)
        {
            return Result.Success();
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Code));
        }
        catch (FormatException)
        {
            return Result.Failure(
                Error.Validation("Auth.InvalidConfirmationToken", "The email confirmation token is malformed or expired."));
        }

        var confirmResult = await _userManager.ConfirmEmailAsync(user, decodedToken);
        if (!confirmResult.Succeeded)
        {
            return Result.Failure(
                Error.Validation(
                    "Auth.ConfirmationFailed",
                    "This confirmation link has expired or is no longer valid. Please request a fresh confirmation email."));
        }

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "auth.email_confirmed",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id),
            cancellationToken);

        if (_adminBootstrapSeeder is not null)
        {
            await _adminBootstrapSeeder.EnsureBootstrapAdminForUserAsync(user.Id, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> ResendConfirmationAsync(
        ResendConfirmationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return Result.Failure(
                Error.Validation("Auth.EmailRequired", "Enter your email address."));
        }

        var email = command.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null || user.EmailConfirmed || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Success();
        }

        await SendConfirmationEmailInternalAsync(user, command.ConfirmationLinkBaseUrl, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<LoginResult>> LoginAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _loginValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<LoginResult>(
                Error.Validation("Auth.ValidationFailed", validation.Errors[0].ErrorMessage));
        }

        var email = command.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return Result.Success(new LoginResult(
                LoginStatus.InvalidCredentials,
                Message: "Invalid email address or password."));
        }

        if (!user.IsActive || user.DeletedAt is not null)
        {
            return Result.Success(new LoginResult(
                LoginStatus.AccountInactive,
                Message: "This account is inactive or scheduled for deletion."));
        }

        if (!user.EmailConfirmed)
        {
            return Result.Success(new LoginResult(
                LoginStatus.EmailNotConfirmed,
                user.AccountType,
                "Please confirm your email address before signing in."));
        }

        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return Result.Success(new LoginResult(
                LoginStatus.PasswordNotSetForSocialAccount,
                user.AccountType,
                "This account was created with Google or GitHub and does not have a password set yet. Sign in with your connected social account, or request a signed Set-Password link below."));
        }

        if (_adminBootstrapSeeder is not null)
        {
            await _adminBootstrapSeeder.EnsureBootstrapAdminForUserAsync(user.Id, cancellationToken);
        }

        var signInResult = await _signInManager.PasswordSignInAsync(
            user,
            command.Password,
            isPersistent: command.RememberMe,
            lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            await _auditLogger.LogAsync(
                new AuditLogEntry(
                    Action: "auth.lockout",
                    EntityType: "user",
                    EntityId: user.Id.ToString(),
                    ActorType: AuditActorType.System,
                    ActorUserId: user.Id),
                cancellationToken);

            return Result.Success(new LoginResult(
                LoginStatus.LockedOut,
                user.AccountType,
                "Too many failed sign-in attempts. Your account is temporarily locked for 15 minutes."));
        }

        if (!signInResult.Succeeded)
        {
            return Result.Success(new LoginResult(
                LoginStatus.InvalidCredentials,
                Message: "Invalid email address or password."));
        }

        user.LastLoginAt = _clock.UtcNow;
        await _userManager.UpdateAsync(user);

        var roles = await GetEffectiveUserRolesAsync(user);
        return Result.Success(new LoginResult(LoginStatus.Succeeded, user.AccountType, Roles: roles));
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _signInManager.SignOutAsync();
    }

    public async Task<Result> SendPasswordResetOrSetupLinkAsync(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _forgotPasswordValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(
                Error.Validation("Auth.ValidationFailed", validation.Errors[0].ErrorMessage));
        }

        var email = command.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null || !user.EmailConfirmed || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Success();
        }

        var isFirstTimePasswordSetup = string.IsNullOrEmpty(user.PasswordHash);
        await SendPasswordLinkInternalAsync(
            user,
            command.ResetLinkBaseUrl,
            isFirstTimePasswordSetup,
            isWelcomeEmail: false,
            providerDisplayName: null,
            cancellationToken);

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _resetPasswordValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(
                Error.Validation("Auth.ValidationFailed", validation.Errors[0].ErrorMessage));
        }

        var email = command.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure(
                Error.Validation("Auth.ResetFailed", "This password link is invalid or has expired."));
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Code));
        }
        catch (FormatException)
        {
            return Result.Failure(
                Error.Validation("Auth.InvalidResetToken", "This password link is malformed or has expired."));
        }

        var resetResult = await _userManager.ResetPasswordAsync(user, decodedToken, command.NewPassword);
        if (!resetResult.Succeeded)
        {
            var message = string.Join(" ", resetResult.Errors.Select(e => e.Description));
            return Result.Failure(Error.Validation("Auth.ResetFailed", message));
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "auth.password_reset",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id),
            cancellationToken);

        return Result.Success();
    }

    public async Task<IReadOnlyList<ExternalProviderDescriptor>> GetConfiguredExternalProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();

        return schemes
            .Select(s => new ExternalProviderDescriptor(
                Scheme: s.Name,
                DisplayName: s.DisplayName ?? s.Name,
                AllowedForStudios: IsProviderAllowedForStudios(s.Name)))
            .ToArray();
    }

    public async Task<Result> ValidateExternalChallengeAsync(
        string provider,
        AccountType? preferredAccountType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return Result.Failure(
                Error.Validation("Auth.ProviderRequired", "Select a social sign-in provider."));
        }

        var providers = await GetConfiguredExternalProvidersAsync(cancellationToken);
        var match = providers.FirstOrDefault(p =>
            string.Equals(p.Scheme, provider, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return Result.Failure(
                Error.Validation(
                    "Auth.ProviderNotConfigured",
                    $"Sign-in with {provider} is not configured in this environment."));
        }

        if (preferredAccountType == AccountType.Studio && !match.AllowedForStudios)
        {
            return Result.Failure(
                Error.Validation(
                    "Auth.ProviderCandidatesOnly",
                    $"{match.DisplayName} sign-in is available for Candidate accounts only. Please use Google or email/password for Studio accounts."));
        }

        return Result.Success();
    }

    public async Task<ExternalLoginCallbackResult> HandleExternalLoginCallbackAsync(
        string? remoteError,
        AccountType? targetPortalRole = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            await ClearExternalCookieAsync();
            return new ExternalLoginCallbackResult(
                ExternalCallbackStatus.RemoteError,
                Message: "External sign-in was cancelled or denied by the provider.");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return new ExternalLoginCallbackResult(
                ExternalCallbackStatus.ExternalInfoMissing,
                Message: "Your external sign-in session expired. Please try signing in again.");
        }

        var providerDisplayName = info.ProviderDisplayName ?? info.LoginProvider;

        if (targetPortalRole == AccountType.Studio && !IsProviderAllowedForStudios(info.LoginProvider))
        {
            await ClearExternalCookieAsync();
            return new ExternalLoginCallbackResult(
                ExternalCallbackStatus.ProviderNotAllowedForAccountType,
                AccountType.Studio,
                ProviderDisplayName: providerDisplayName,
                Message: $"{providerDisplayName} sign-in is available on the Candidate portal only. Use Google or email/password on the Studio portal.");
        }

        // 1. Check whether this external login is already linked to a HireGameDev user in user_logins.
        var linkedUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (linkedUser is not null)
        {
            if (!linkedUser.IsActive || linkedUser.DeletedAt is not null)
            {
                await ClearExternalCookieAsync();
                return new ExternalLoginCallbackResult(
                    ExternalCallbackStatus.AccountInactive,
                    ProviderDisplayName: providerDisplayName,
                    Message: "This account is inactive or scheduled for deletion.");
            }

            if (_adminBootstrapSeeder is not null)
            {
                await _adminBootstrapSeeder.EnsureBootstrapAdminForUserAsync(linkedUser.Id, cancellationToken);
            }

            var linkedRoles = await GetEffectiveUserRolesAsync(linkedUser);
            if (!IsProviderAllowedForRoles(info.LoginProvider, linkedRoles))
            {
                await ClearExternalCookieAsync();
                return new ExternalLoginCallbackResult(
                    ExternalCallbackStatus.ProviderNotAllowedForAccountType,
                    linkedUser.AccountType,
                    linkedUser.Email,
                    providerDisplayName,
                    $"{providerDisplayName} sign-in requires Candidate access on your account.",
                    linkedRoles);
            }

            var signInResult = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider,
                info.ProviderKey,
                isPersistent: false,
                bypassTwoFactor: false);

            await ClearExternalCookieAsync();

            if (signInResult.IsLockedOut)
            {
                return new ExternalLoginCallbackResult(
                    ExternalCallbackStatus.LockedOut,
                    linkedUser.AccountType,
                    linkedUser.Email,
                    providerDisplayName,
                    "Too many failed sign-in attempts. Your account is temporarily locked for 15 minutes.",
                    linkedRoles);
            }

            if (!signInResult.Succeeded)
            {
                return new ExternalLoginCallbackResult(
                    ExternalCallbackStatus.RemoteError,
                    linkedUser.AccountType,
                    linkedUser.Email,
                    providerDisplayName,
                    $"Could not sign in with {providerDisplayName}.",
                    linkedRoles);
            }

            linkedUser.LastLoginAt = _clock.UtcNow;
            await _userManager.UpdateAsync(linkedUser);

            return new ExternalLoginCallbackResult(
                ExternalCallbackStatus.SignedIn,
                linkedUser.AccountType,
                linkedUser.Email,
                providerDisplayName,
                Roles: linkedRoles);
        }

        // 2. Not linked yet: verify that the external provider returned a verified email address.
        var email = ExtractEmail(info);
        if (string.IsNullOrWhiteSpace(email) || !IsExternalEmailVerified(info))
        {
            await ClearExternalCookieAsync();
            return new ExternalLoginCallbackResult(
                ExternalCallbackStatus.EmailMissingOrUnverified,
                ProviderDisplayName: providerDisplayName,
                Message: $"Your {providerDisplayName} account did not provide a verified email address. Please verify your email in {providerDisplayName} or register with email and password.");
        }

        // 3. Check whether an account with this email already exists in users.
        var existingEmailUser = await _userManager.FindByEmailAsync(email);
        if (existingEmailUser is not null)
        {
            if (!existingEmailUser.IsActive || existingEmailUser.DeletedAt is not null)
            {
                await ClearExternalCookieAsync();
                return new ExternalLoginCallbackResult(
                    ExternalCallbackStatus.AccountInactive,
                    Email: email,
                    ProviderDisplayName: providerDisplayName,
                    Message: "This account is inactive or scheduled for deletion.");
            }

            if (existingEmailUser.EmailConfirmed)
            {
                var existingRoles = await GetEffectiveUserRolesAsync(existingEmailUser);
                if (!IsProviderAllowedForRoles(info.LoginProvider, existingRoles))
                {
                    await ClearExternalCookieAsync();
                    return new ExternalLoginCallbackResult(
                        ExternalCallbackStatus.ProviderNotAllowedForAccountType,
                        existingEmailUser.AccountType,
                        email,
                        providerDisplayName,
                        $"{providerDisplayName} sign-in is available for Candidate accounts only. Use a separate Candidate account with a different email.",
                        existingRoles);
                }

                var addLoginResult = await _userManager.AddLoginAsync(existingEmailUser, info);
                if (!addLoginResult.Succeeded)
                {
                    await ClearExternalCookieAsync();
                    var message = string.Join(" ", addLoginResult.Errors.Select(e => e.Description));
                    return new ExternalLoginCallbackResult(
                        ExternalCallbackStatus.RemoteError,
                        existingEmailUser.AccountType,
                        email,
                        providerDisplayName,
                        message,
                        existingRoles);
                }

                existingEmailUser.LastLoginAt = _clock.UtcNow;
                await _userManager.UpdateAsync(existingEmailUser);
                await PersistExternalProfileMetadataAsync(existingEmailUser, info);

                await _auditLogger.LogAsync(
                    new AuditLogEntry(
                        Action: "auth.external_link",
                        EntityType: "user",
                        EntityId: existingEmailUser.Id.ToString(),
                        ActorType: AuditActorType.User,
                        ActorUserId: existingEmailUser.Id,
                        AfterData: new { provider = info.LoginProvider }),
                    cancellationToken);

                await TrySendSecurityNoticeEmailAsync(
                    existingEmailUser,
                    subject: $"{providerDisplayName} connected to your HireGameDev account",
                    summaryLine: $"{providerDisplayName} was linked as a sign-in method on your HireGameDev account.",
                    tag: "security_external_linked",
                    cancellationToken);

                if (_adminBootstrapSeeder is not null)
                {
                    await _adminBootstrapSeeder.EnsureBootstrapAdminForUserAsync(existingEmailUser.Id, cancellationToken);
                    existingRoles = await GetEffectiveUserRolesAsync(existingEmailUser);
                }

                await _signInManager.SignInAsync(existingEmailUser, isPersistent: false, info.LoginProvider);
                await ClearExternalCookieAsync();

                return new ExternalLoginCallbackResult(
                    ExternalCallbackStatus.SignedIn,
                    existingEmailUser.AccountType,
                    email,
                    providerDisplayName,
                    Roles: existingRoles);
            }

            if (!existingEmailUser.EmailConfirmed && existingEmailUser.LastLoginAt is null)
            {
                await _userManager.DeleteAsync(existingEmailUser);
            }
        }

        // 4. Brand-new external user: keep hgd.external cookie active and proceed to "Complete Your Account".
        return new ExternalLoginCallbackResult(
            ExternalCallbackStatus.RequiresAccountCompletion,
            Email: email,
            ProviderDisplayName: providerDisplayName);
    }

    public async Task<Result<PendingExternalRegistration>> GetPendingExternalRegistrationAsync(
        CancellationToken cancellationToken = default)
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return Result.Failure<PendingExternalRegistration>(
                Error.Validation(
                    "Auth.ExternalSessionExpired",
                    "Your external sign-in session expired. Please start sign-in again."));
        }

        var providerDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        var email = ExtractEmail(info);
        if (string.IsNullOrWhiteSpace(email) || !IsExternalEmailVerified(info))
        {
            await ClearExternalCookieAsync();
            return Result.Failure<PendingExternalRegistration>(
                Error.Validation(
                    "Auth.UnverifiedExternalEmail",
                    $"Your {providerDisplayName} account did not supply a verified email address."));
        }

        var suggestedName = ExtractSuggestedDisplayName(info, email);

        return Result.Success(new PendingExternalRegistration(
            Provider: info.LoginProvider,
            ProviderDisplayName: providerDisplayName,
            Email: email,
            SuggestedDisplayName: suggestedName,
            AllowedForStudios: IsProviderAllowedForStudios(info.LoginProvider)));
    }

    public async Task<Result<CompleteExternalRegistrationResult>> CompleteExternalRegistrationAsync(
        CompleteExternalRegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _completeExternalValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<CompleteExternalRegistrationResult>(
                Error.Validation("Auth.ValidationFailed", validation.Errors[0].ErrorMessage));
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return Result.Failure<CompleteExternalRegistrationResult>(
                Error.Validation(
                    "Auth.ExternalSessionExpired",
                    "Your external sign-in session expired. Please sign in again."));
        }

        var providerDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        if (!IsProviderAllowedForAccountType(info.LoginProvider, command.AccountType))
        {
            return Result.Failure<CompleteExternalRegistrationResult>(
                Error.Validation(
                    "Auth.ProviderCandidatesOnly",
                    $"{providerDisplayName} sign-in is available for Candidate accounts only. Choose Candidate or register your Studio with Google or email/password."));
        }

        var email = ExtractEmail(info);
        if (string.IsNullOrWhiteSpace(email) || !IsExternalEmailVerified(info))
        {
            await ClearExternalCookieAsync();
            return Result.Failure<CompleteExternalRegistrationResult>(
                Error.Validation(
                    "Auth.UnverifiedExternalEmail",
                    $"Your {providerDisplayName} account did not supply a verified email address."));
        }

        var existingByLogin = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (existingByLogin is not null)
        {
            await _signInManager.SignInAsync(existingByLogin, isPersistent: false, info.LoginProvider);
            await ClearExternalCookieAsync();
            var existingRoles = await GetEffectiveUserRolesAsync(existingByLogin);
            return Result.Success(new CompleteExternalRegistrationResult(
                existingByLogin.Id,
                existingByLogin.Email ?? email,
                existingByLogin.AccountType,
                existingRoles));
        }

        var existingByEmail = await _userManager.FindByEmailAsync(email);
        if (existingByEmail is not null)
        {
            if (!existingByEmail.EmailConfirmed && existingByEmail.LastLoginAt is null)
            {
                await _userManager.DeleteAsync(existingByEmail);
            }
            else
            {
                await ClearExternalCookieAsync();
                return Result.Failure<CompleteExternalRegistrationResult>(
                    Error.Conflict(
                        "Auth.EmailAlreadyExists",
                        "An account with this email address already exists. Please sign in instead."));
            }
        }

        await EnsureCoreRolesExistAsync();

        var now = _clock.UtcNow;
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = command.DisplayName.Trim(),
            AccountType = command.AccountType,
            IsActive = true,
            ThemePreference = ThemePreference.System,
            Timezone = "Asia/Kolkata",
            AgeConfirmedAt = now,
            LastLoginAt = now
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            var message = string.Join(" ", createResult.Errors.Select(e => e.Description));
            return Result.Failure<CompleteExternalRegistrationResult>(
                Error.Validation("Auth.ExternalRegistrationFailed", message));
        }

        var roleName = SnakeCaseEnumValueConverter<AccountType>.ToSnakeCaseValue(command.AccountType);
        await _userManager.AddToRoleAsync(user, roleName);

        var addLoginResult = await _userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            var message = string.Join(" ", addLoginResult.Errors.Select(e => e.Description));
            return Result.Failure<CompleteExternalRegistrationResult>(
                Error.Validation("Auth.ExternalLinkFailed", message));
        }

        await PersistExternalProfileMetadataAsync(user, info);

        await _consentService.RecordRegistrationConsentsAsync(user.Id, cancellationToken: cancellationToken);

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "auth.external_register",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id,
                AfterData: new { accountType = roleName, provider = info.LoginProvider }),
            cancellationToken);

        if (_adminBootstrapSeeder is not null)
        {
            await _adminBootstrapSeeder.EnsureBootstrapAdminForUserAsync(user.Id, cancellationToken);
        }

        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
        await ClearExternalCookieAsync();

        if (!string.IsNullOrWhiteSpace(command.SetPasswordLinkBaseUrl))
        {
            try
            {
                await SendPasswordLinkInternalAsync(
                    user,
                    command.SetPasswordLinkBaseUrl,
                    isFirstTimePasswordSetup: true,
                    isWelcomeEmail: true,
                    providerDisplayName: providerDisplayName,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Welcome/set-password email could not be sent after external registration for user {UserId}.",
                    user.Id);
            }
        }

        var roles = await GetEffectiveUserRolesAsync(user);
        return Result.Success(new CompleteExternalRegistrationResult(user.Id, email, user.AccountType, roles));
    }

    public async Task<Result> UpdateThemePreferenceAsync(
        Guid userId,
        ThemePreference themePreference,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(
                Error.Validation("Auth.InvalidUserId", "User ID cannot be empty."));
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure(
                Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        if (user.ThemePreference != themePreference)
        {
            user.ThemePreference = themePreference;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                var message = string.Join(" ", updateResult.Errors.Select(e => e.Description));
                return Result.Failure(
                    Error.Failure("Auth.ThemeUpdateFailed", message));
            }

            await _signInManager.RefreshSignInAsync(user);
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<string>>> EnablePortalRoleAsync(
        Guid userId,
        AccountType targetRole,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<string>>(
                Error.Validation("Auth.InvalidUserId", "User ID cannot be empty."));
        }

        if (targetRole is not (AccountType.Candidate or AccountType.Studio))
        {
            return Result.Failure<IReadOnlyList<string>>(
                Error.Validation("Auth.InvalidPortalRole", "Only Candidate or Studio workspaces can be self-enabled."));
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure<IReadOnlyList<string>>(
                Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        if (user.AccountType != targetRole)
        {
            return Result.Failure<IReadOnlyList<string>>(
                Error.Forbidden("Auth.SeparateAccountRequired",
                    "Candidate and Studio accounts are separate. Use a different email to register the other account type."));
        }

        return Result.Success(await GetEffectiveUserRolesAsync(user));
    }

    public async Task<Result<AccountManageState>> GetAccountManageStateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<AccountManageState>(
                Error.Validation("Auth.InvalidUserId", "User ID cannot be empty."));
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure<AccountManageState>(
                Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        var roles = await GetEffectiveUserRolesAsync(user);
        var logins = await _userManager.GetLoginsAsync(user);
        var availableProviders = await GetConfiguredExternalProvidersAsync(cancellationToken);
        var consents = await _consentService.GetUserConsentsAsync(user.Id, cancellationToken);

        var currentTermsVersion = await _settingsService.GetStringAsync(
            AppSettingKeys.LegalTermsVersion,
            AppSettingKeys.DefaultLegalVersion,
            cancellationToken);
        var currentPrivacyVersion = await _settingsService.GetStringAsync(
            AppSettingKeys.LegalPrivacyVersion,
            AppSettingKeys.DefaultLegalVersion,
            cancellationToken);

        var hasAcceptedTerms = await _consentService.HasAcceptedVersionAsync(
            user.Id,
            ConsentType.Terms,
            currentTermsVersion,
            cancellationToken);
        var hasAcceptedPrivacy = await _consentService.HasAcceptedVersionAsync(
            user.Id,
            ConsentType.Privacy,
            currentPrivacyVersion,
            cancellationToken);

        var linkedItems = logins
            .Select(l => new LinkedExternalLoginItem(
                LoginProvider: l.LoginProvider,
                ProviderDisplayName: string.IsNullOrWhiteSpace(l.ProviderDisplayName) ? l.LoginProvider : l.ProviderDisplayName,
                ProviderKey: l.ProviderKey))
            .ToArray();

        return Result.Success(new AccountManageState(
            UserId: user.Id,
            Email: user.Email ?? string.Empty,
            DisplayName: user.DisplayName,
            PrimaryAccountType: user.AccountType,
            Roles: roles,
            ThemePreference: user.ThemePreference,
            HasPassword: !string.IsNullOrEmpty(user.PasswordHash),
            IsRecentlyAuthenticated: IsWithinRecentAuthWindow(user),
            LastLoginAt: user.LastLoginAt,
            LinkedLogins: linkedItems,
            AvailableProviders: availableProviders,
            Consents: consents,
            CurrentTermsVersion: currentTermsVersion,
            CurrentPrivacyVersion: currentPrivacyVersion,
            HasAcceptedCurrentTerms: hasAcceptedTerms,
            HasAcceptedCurrentPrivacy: hasAcceptedPrivacy));
    }

    public async Task<Result> UpdateDisplayNameAsync(
        UpdateDisplayNameCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _updateDisplayNameValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(
                Error.Validation("Auth.ValidationFailed", validation.Errors[0].ErrorMessage));
        }

        var user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure(
                Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        var newDisplayName = command.DisplayName.Trim();
        if (string.Equals(user.DisplayName, newDisplayName, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        var previousName = user.DisplayName;
        user.DisplayName = newDisplayName;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var message = string.Join(" ", updateResult.Errors.Select(e => e.Description));
            return Result.Failure(Error.Failure("Auth.ProfileUpdateFailed", message));
        }

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "auth.display_name_updated",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id,
                BeforeData: new { displayName = previousName },
                AfterData: new { displayName = newDisplayName }),
            cancellationToken);

        await _signInManager.RefreshSignInAsync(user);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _changePasswordValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(
                Error.Validation("Auth.ValidationFailed", validation.Errors[0].ErrorMessage));
        }

        var user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure(
                Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return Result.Failure(
                Error.Validation(
                    "Auth.PasswordNotSet",
                    "Your account does not have a password yet. Request a signed Set-Password link instead."));
        }

        var changeResult = await _userManager.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!changeResult.Succeeded)
        {
            var message = string.Join(" ", changeResult.Errors.Select(e => e.Description));
            return Result.Failure(Error.Validation("Auth.PasswordChangeFailed", message));
        }

        user.LastLoginAt = _clock.UtcNow;
        await _userManager.UpdateAsync(user);

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "auth.password_changed",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id),
            cancellationToken);

        await TrySendSecurityNoticeEmailAsync(
            user,
            subject: "Your HireGameDev password was changed",
            summaryLine: "Your HireGameDev account password was updated.",
            tag: "security_password_changed",
            cancellationToken);

        await _signInManager.RefreshSignInAsync(user);
        return Result.Success();
    }

    public async Task<Result> AuthorizeExternalLinkAsync(
        Guid userId,
        string provider,
        string? currentPassword = null,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(provider))
        {
            return Result.Failure(
                Error.Validation("Auth.ProviderRequired", "Select a social sign-in provider."));
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure(
                Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        var reauthCheck = await VerifySensitiveActionAuthAsync(user, currentPassword);
        if (reauthCheck.IsFailure)
        {
            return reauthCheck;
        }

        var providers = await GetConfiguredExternalProvidersAsync(cancellationToken);
        var match = providers.FirstOrDefault(p =>
            string.Equals(p.Scheme, provider, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return Result.Failure(
                Error.Validation("Auth.ProviderNotConfigured", $"Sign-in with {provider} is not configured."));
        }

        var roles = await GetEffectiveUserRolesAsync(user);
        if (!IsProviderAllowedForRoles(match.Scheme, roles))
        {
            return Result.Failure(
                Error.Validation(
                    "Auth.ProviderCandidatesOnly",
                    $"{match.DisplayName} can only be linked when Candidate access is enabled on your account."));
        }

        return Result.Success();
    }

    public async Task<Result> CompleteExternalLinkAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            await ClearExternalCookieAsync();
            return Result.Failure(Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        var info = await _signInManager.GetExternalLoginInfoAsync(userId.ToString());
        if (info is null)
        {
            await ClearExternalCookieAsync();
            return Result.Failure(
                Error.Validation("Auth.ExternalSessionExpired", "The external account link session expired. Please try again."));
        }

        var providerDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        var roles = await GetEffectiveUserRolesAsync(user);
        if (!IsProviderAllowedForRoles(info.LoginProvider, roles))
        {
            await ClearExternalCookieAsync();
            return Result.Failure(
                Error.Validation(
                    "Auth.ProviderCandidatesOnly",
                    $"{providerDisplayName} is restricted to accounts with Candidate access enabled."));
        }

        if (!IsExternalEmailVerified(info))
        {
            await ClearExternalCookieAsync();
            return Result.Failure(
                Error.Validation(
                    "Auth.UnverifiedExternalEmail",
                    $"Your {providerDisplayName} account did not supply a verified email address."));
        }

        var alreadyLinkedOwner = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (alreadyLinkedOwner is not null)
        {
            await ClearExternalCookieAsync();
            if (alreadyLinkedOwner.Id == user.Id)
            {
                return Result.Success();
            }

            return Result.Failure(
                Error.Conflict(
                    "Auth.ExternalAlreadyLinked",
                    $"This {providerDisplayName} account is already linked to another HireGameDev account."));
        }

        var addResult = await _userManager.AddLoginAsync(user, info);
        await ClearExternalCookieAsync();

        if (!addResult.Succeeded)
        {
            var message = string.Join(" ", addResult.Errors.Select(e => e.Description));
            return Result.Failure(Error.Validation("Auth.ExternalLinkFailed", message));
        }

        await PersistExternalProfileMetadataAsync(user, info);

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "auth.external_link",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id,
                AfterData: new { provider = info.LoginProvider }),
            cancellationToken);

        await TrySendSecurityNoticeEmailAsync(
            user,
            subject: $"{providerDisplayName} connected to your HireGameDev account",
            summaryLine: $"{providerDisplayName} was linked as a sign-in method on your HireGameDev account.",
            tag: "security_external_linked",
            cancellationToken);

        await _signInManager.RefreshSignInAsync(user);
        return Result.Success();
    }

    public async Task<Result> UnlinkExternalLoginAsync(
        UnlinkExternalLoginCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.LoginProvider) ||
            string.IsNullOrWhiteSpace(command.ProviderKey))
        {
            return Result.Failure(
                Error.Validation("Auth.InvalidUnlinkRequest", "Provider details are required to unlink a sign-in method."));
        }

        var user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure(Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        var reauthCheck = await VerifySensitiveActionAuthAsync(user, command.CurrentPassword);
        if (reauthCheck.IsFailure)
        {
            return reauthCheck;
        }

        var logins = await _userManager.GetLoginsAsync(user);
        var hasPassword = !string.IsNullOrEmpty(user.PasswordHash);
        var totalMethods = (hasPassword ? 1 : 0) + logins.Count;
        if (totalMethods <= 1)
        {
            return Result.Failure(
                Error.Validation(
                    "Auth.LastSignInMethod",
                    "You cannot remove your only remaining sign-in method. Set a password or connect another provider first."));
        }

        var removeResult = await _userManager.RemoveLoginAsync(user, command.LoginProvider, command.ProviderKey);
        if (!removeResult.Succeeded)
        {
            var message = string.Join(" ", removeResult.Errors.Select(e => e.Description));
            return Result.Failure(Error.Failure("Auth.ExternalUnlinkFailed", message));
        }

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "auth.external_unlink",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id,
                BeforeData: new { provider = command.LoginProvider }),
            cancellationToken);

        await TrySendSecurityNoticeEmailAsync(
            user,
            subject: $"{command.LoginProvider} removed from your HireGameDev account",
            summaryLine: $"{command.LoginProvider} was unlinked from your HireGameDev sign-in methods.",
            tag: "security_external_unlinked",
            cancellationToken);

        await _signInManager.RefreshSignInAsync(user);
        return Result.Success();
    }

    public async Task<Result> AcceptCurrentLegalConsentsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Auth.InvalidUserId", "User ID cannot be empty."));
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Failure(Error.NotFound("Auth.UserNotFound", "User account was not found."));
        }

        await _consentService.RecordRegistrationConsentsAsync(user.Id, cancellationToken: cancellationToken);

        await _auditLogger.LogAsync(
            new AuditLogEntry(
                Action: "consent.reaccepted",
                EntityType: "user",
                EntityId: user.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: user.Id),
            cancellationToken);

        return Result.Success();
    }

    private async Task<IReadOnlyList<string>> GetEffectiveUserRolesAsync(ApplicationUser user)
    {
        var dbRoles = await _userManager.GetRolesAsync(user);
        var primaryRole = SnakeCaseEnumValueConverter<AccountType>.ToSnakeCaseValue(user.AccountType);

        var set = new HashSet<string>(dbRoles.Where(r =>
            !string.Equals(r, HireGameDevRoleNames.Candidate, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(r, HireGameDevRoleNames.Studio, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase)
        {
            primaryRole
        };

        return set
            .Select(r => r.ToLowerInvariant())
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();
    }

    private bool IsWithinRecentAuthWindow(ApplicationUser user)
    {
        if (!user.LastLoginAt.HasValue)
        {
            return false;
        }

        var elapsed = _clock.UtcNow - user.LastLoginAt.Value;
        return elapsed >= TimeSpan.Zero && elapsed <= SensitiveActionReauthWindow;
    }

    private async Task<Result> VerifySensitiveActionAuthAsync(ApplicationUser user, string? currentPassword)
    {
        if (IsWithinRecentAuthWindow(user))
        {
            return Result.Success();
        }

        if (!string.IsNullOrEmpty(user.PasswordHash) && !string.IsNullOrWhiteSpace(currentPassword))
        {
            var passwordValid = await _userManager.CheckPasswordAsync(user, currentPassword);
            if (!passwordValid)
            {
                return Result.Failure(
                    Error.Validation("Auth.InvalidPassword", "Current password is incorrect."));
            }

            user.LastLoginAt = _clock.UtcNow;
            await _userManager.UpdateAsync(user);
            return Result.Success();
        }

        return Result.Failure(
            Error.Validation(
                "Auth.ReauthenticationRequired",
                !string.IsNullOrEmpty(user.PasswordHash)
                    ? "For security, confirm your current password (or sign in again within the last 10 minutes) before changing sign-in methods."
                    : "For security, please sign out and sign back in within the last 10 minutes before changing connected sign-in methods."));
    }

    private static bool IsProviderAllowedForStudios(string providerScheme)
    {
        return !string.Equals(providerScheme, GitHubProviderScheme, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProviderAllowedForAccountType(string providerScheme, AccountType accountType)
    {
        if (accountType == AccountType.Studio && !IsProviderAllowedForStudios(providerScheme))
        {
            return false;
        }

        return true;
    }

    private static bool IsProviderAllowedForRoles(string providerScheme, IReadOnlyList<string> roles)
    {
        if (IsProviderAllowedForStudios(providerScheme))
        {
            return true;
        }

        return roles.Any(r =>
            string.Equals(r, HireGameDevRoleNames.Candidate, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r, HireGameDevRoleNames.Admin, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractEmail(ExternalLoginInfo info)
    {
        var email = info.Principal.FindFirstValue(ClaimTypes.Email)
            ?? info.Principal.FindFirstValue("email");

        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    private static bool IsExternalEmailVerified(ExternalLoginInfo info)
    {
        var verifiedClaim = info.Principal.FindFirstValue(EmailVerifiedClaimType);
        return bool.TryParse(verifiedClaim, out var isVerified) && isVerified;
    }

    private static string ExtractSuggestedDisplayName(ExternalLoginInfo info, string fallbackEmail)
    {
        var candidateName = info.Principal.FindFirstValue(ClaimTypes.Name)
            ?? info.Principal.FindFirstValue("name");

        if (string.IsNullOrWhiteSpace(candidateName))
        {
            var atIndex = fallbackEmail.IndexOf('@');
            candidateName = atIndex > 0 ? fallbackEmail[..atIndex] : fallbackEmail;
        }

        candidateName = candidateName.Trim();
        if (candidateName.Length > 80)
        {
            candidateName = candidateName[..80].TrimEnd();
        }

        return candidateName.Length >= 2 ? candidateName : "Game Developer";
    }

    private async Task ClearExternalCookieAsync()
    {
        if (_signInManager.Context is not null)
        {
            await _signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    private async Task PersistExternalProfileMetadataAsync(ApplicationUser user, ExternalLoginInfo info)
    {
        if (!string.Equals(info.LoginProvider, GitHubProviderScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var gitHubLogin = info.Principal.FindFirstValue(HireGameDevClaimTypes.GitHubLogin);
        if (!string.IsNullOrWhiteSpace(gitHubLogin))
        {
            await _userManager.SetAuthenticationTokenAsync(user, GitHubProviderScheme, "login", gitHubLogin);
        }

        var gitHubProfileUrl = info.Principal.FindFirstValue(HireGameDevClaimTypes.GitHubProfileUrl);
        if (!string.IsNullOrWhiteSpace(gitHubProfileUrl))
        {
            await _userManager.SetAuthenticationTokenAsync(user, GitHubProviderScheme, "profile_url", gitHubProfileUrl);
        }
    }

    private async Task TrySendSecurityNoticeEmailAsync(
        ApplicationUser user,
        string subject,
        string summaryLine,
        string tag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        try
        {
            var encodedName = WebUtility.HtmlEncode(user.DisplayName);
            var encodedSummary = WebUtility.HtmlEncode(summaryLine);
            var htmlBody = $"""
                <h2>Security notice for your HireGameDev account</h2>
                <p>Hi {encodedName},</p>
                <p>{encodedSummary}</p>
                <p>If you made this change, no further action is required. If you did not authorize this change, reset your password immediately and contact us.</p>
                """;
            var textBody =
                $"Hi {user.DisplayName},\n\n{summaryLine}\n\nIf you did not authorize this change, reset your password immediately.\n";

            await _emailSender.SendAsync(
                new EmailMessage(
                    To: user.Email,
                    Subject: subject,
                    HtmlBody: htmlBody,
                    TextBody: textBody,
                    Tag: tag),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Security notification email ({Tag}) failed for user {UserId}.", tag, user.Id);
        }
    }

    private async Task SendConfirmationEmailInternalAsync(
        ApplicationUser user,
        string confirmationLinkBaseUrl,
        CancellationToken cancellationToken)
    {
        var rawToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));

        var confirmationUrl =
            $"{confirmationLinkBaseUrl}?userId={user.Id}&code={Uri.EscapeDataString(encodedToken)}";

        var encodedName = WebUtility.HtmlEncode(user.DisplayName);
        var encodedUrl = WebUtility.HtmlEncode(confirmationUrl);
        var accountLabel = user.AccountType == AccountType.Studio ? "studio" : "candidate";

        var htmlBody = $"""
            <h2>Confirm your email address</h2>
            <p>Hi {encodedName},</p>
            <p>Welcome to HireGameDev! Confirm your email address to activate your {accountLabel} account:</p>
            <p><a href="{encodedUrl}" style="display:inline-block;padding:10px 18px;background:#f5c400;color:#14130f;text-decoration:none;border-radius:8px;font-weight:600;">Confirm email</a></p>
            <p>If you did not sign up for HireGameDev, you can ignore this message.</p>
            """;

        var textBody =
            $"Hi {user.DisplayName},\n\nConfirm your HireGameDev {accountLabel} account:\n{confirmationUrl}\n";

        await _emailSender.SendAsync(
            new EmailMessage(
                To: user.Email!,
                Subject: "Confirm your HireGameDev email",
                HtmlBody: htmlBody,
                TextBody: textBody,
                Tag: "confirm_email"),
            cancellationToken);
    }

    private async Task SendPasswordLinkInternalAsync(
        ApplicationUser user,
        string resetLinkBaseUrl,
        bool isFirstTimePasswordSetup,
        bool isWelcomeEmail,
        string? providerDisplayName,
        CancellationToken cancellationToken)
    {
        var email = user.Email!;
        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));

        var mode = isFirstTimePasswordSetup ? "set" : "reset";
        var actionUrl =
            $"{resetLinkBaseUrl}?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(encodedToken)}&mode={mode}";

        var encodedName = WebUtility.HtmlEncode(user.DisplayName);
        var encodedUrl = WebUtility.HtmlEncode(actionUrl);
        var encodedProvider = WebUtility.HtmlEncode(providerDisplayName ?? "your social account");
        var accountLabel = user.AccountType == AccountType.Studio ? "studio" : "candidate";

        string subject;
        string htmlBody;
        string textBody;
        string tag;

        if (isWelcomeEmail)
        {
            subject = "Welcome to HireGameDev — optional password setup";
            htmlBody = $"""
                <h2>Welcome to HireGameDev!</h2>
                <p>Hi {encodedName},</p>
                <p>Your HireGameDev {accountLabel} account is ready and connected to {encodedProvider}.</p>
                <p>You can continue signing in with {encodedProvider} anytime. If you would also like to sign in directly with your email and a password, use this optional secure link:</p>
                <p><a href="{encodedUrl}" style="display:inline-block;padding:10px 18px;background:#f5c400;color:#14130f;text-decoration:none;border-radius:8px;font-weight:600;">Set a password (optional)</a></p>
                <p>If you prefer social sign-in only, no action is needed.</p>
                """;
            textBody =
                $"Hi {user.DisplayName},\n\nWelcome to HireGameDev! Your {accountLabel} account is connected to {providerDisplayName ?? "your social account"}.\n\nOptional: set a password for email sign-in:\n{actionUrl}\n";
            tag = "welcome_sso_set_password";
        }
        else
        {
            subject = isFirstTimePasswordSetup
                ? "Set a password for your HireGameDev account"
                : "Reset your HireGameDev password";

            var heading = isFirstTimePasswordSetup
                ? "Set a password for email sign-in"
                : "Reset your password";

            var buttonLabel = isFirstTimePasswordSetup
                ? "Set password"
                : "Choose a new password";

            htmlBody = $"""
                <h2>{heading}</h2>
                <p>Hi {encodedName},</p>
                <p>Use the secure link below to {(isFirstTimePasswordSetup ? "add a password to your HireGameDev account" : "reset your HireGameDev password")}:</p>
                <p><a href="{encodedUrl}" style="display:inline-block;padding:10px 18px;background:#f5c400;color:#14130f;text-decoration:none;border-radius:8px;font-weight:600;">{buttonLabel}</a></p>
                <p>If you did not request this email, you can safely ignore it.</p>
                """;
            textBody =
                $"Hi {user.DisplayName},\n\nOpen this link to {(isFirstTimePasswordSetup ? "set" : "reset")} your HireGameDev password:\n{actionUrl}\n\nIf you did not request this, ignore this email.";
            tag = isFirstTimePasswordSetup ? "set_password" : "reset_password";
        }

        await _emailSender.SendAsync(
            new EmailMessage(
                To: email,
                Subject: subject,
                HtmlBody: htmlBody,
                TextBody: textBody,
                Tag: tag),
            cancellationToken);
    }

    private async Task EnsureCoreRolesExistAsync()
    {
        foreach (var roleName in CoreRoleNames)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                await _roleManager.CreateAsync(new ApplicationRole { Name = roleName });
            }
        }
    }
}
