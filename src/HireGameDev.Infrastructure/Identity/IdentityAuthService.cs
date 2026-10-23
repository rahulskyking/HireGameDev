using System.Net;
using System.Text;
using FluentValidation;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Auth;
using HireGameDev.Application.Abstractions.Email;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Auth;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace HireGameDev.Infrastructure.Identity;

internal sealed class IdentityAuthService : IAuthService
{
    private static readonly string[] CoreRoleNames = ["candidate", "studio", "admin"];

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IEmailSender _emailSender;
    private readonly IClock _clock;
    private readonly IValidator<RegisterCommand> _registerValidator;
    private readonly IValidator<LoginCommand> _loginValidator;
    private readonly IValidator<ForgotPasswordCommand> _forgotPasswordValidator;
    private readonly IValidator<ResetPasswordCommand> _resetPasswordValidator;

    public IdentityAuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        IEmailSender emailSender,
        IClock clock,
        IValidator<RegisterCommand> registerValidator,
        IValidator<LoginCommand> loginValidator,
        IValidator<ForgotPasswordCommand> forgotPasswordValidator,
        IValidator<ResetPasswordCommand> resetPasswordValidator)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _emailSender = emailSender;
        _clock = clock;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _forgotPasswordValidator = forgotPasswordValidator;
        _resetPasswordValidator = resetPasswordValidator;
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
                    "An account with this email address already exists. Please sign in instead."));
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

        // Do not reveal whether an email exists or is already confirmed.
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

        // ADR-006 Smart Login Prompt: guide SSO-only accounts cleanly when no password has been set yet.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return Result.Success(new LoginResult(
                LoginStatus.PasswordNotSetForSocialAccount,
                user.AccountType,
                "This account was created with Google or GitHub and does not have a password set yet. Sign in with your connected social account, or request a signed Set-Password link below."));
        }

        var signInResult = await _signInManager.PasswordSignInAsync(
            user,
            command.Password,
            isPersistent: command.RememberMe,
            lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
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

        return Result.Success(new LoginResult(LoginStatus.Succeeded, user.AccountType));
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

        // Prevent account enumeration: silently succeed when user does not exist or is inactive.
        if (user is null || !user.EmailConfirmed || !user.IsActive || user.DeletedAt is not null)
        {
            return Result.Success();
        }

        var isFirstTimePasswordSetup = string.IsNullOrEmpty(user.PasswordHash);
        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));

        var mode = isFirstTimePasswordSetup ? "set" : "reset";
        var actionUrl =
            $"{command.ResetLinkBaseUrl}?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(encodedToken)}&mode={mode}";

        var encodedName = WebUtility.HtmlEncode(user.DisplayName);
        var encodedUrl = WebUtility.HtmlEncode(actionUrl);

        var subject = isFirstTimePasswordSetup
            ? "Set a password for your HireGameDev account"
            : "Reset your HireGameDev password";

        var heading = isFirstTimePasswordSetup
            ? "Set a password for email sign-in"
            : "Reset your password";

        var buttonLabel = isFirstTimePasswordSetup
            ? "Set password"
            : "Choose a new password";

        var htmlBody = $"""
            <h2>{heading}</h2>
            <p>Hi {encodedName},</p>
            <p>Use the secure link below to {(isFirstTimePasswordSetup ? "add a password to your HireGameDev account" : "reset your HireGameDev password")}:</p>
            <p><a href="{encodedUrl}" style="display:inline-block;padding:10px 18px;background:#f5c400;color:#14130f;text-decoration:none;border-radius:8px;font-weight:600;">{buttonLabel}</a></p>
            <p>If you did not request this email, you can safely ignore it.</p>
            """;

        var textBody =
            $"Hi {user.DisplayName},\n\nOpen this link to {(isFirstTimePasswordSetup ? "set" : "reset")} your HireGameDev password:\n{actionUrl}\n\nIf you did not request this, ignore this email.";

        await _emailSender.SendAsync(
            new EmailMessage(
                To: email,
                Subject: subject,
                HtmlBody: htmlBody,
                TextBody: textBody,
                Tag: isFirstTimePasswordSetup ? "set_password" : "reset_password"),
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

        return Result.Success();
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
