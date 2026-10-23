using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Auth;

public sealed record RegisterCommand(
    AccountType AccountType,
    string DisplayName,
    string Email,
    string Password,
    bool ConfirmAge18Plus,
    bool AcceptTermsAndPrivacy,
    string ConfirmationLinkBaseUrl);

public sealed record RegisterResult(
    Guid UserId,
    string Email,
    bool ConfirmationEmailSent);

public sealed record ConfirmEmailCommand(
    Guid UserId,
    string Code);

public sealed record ResendConfirmationCommand(
    string Email,
    string ConfirmationLinkBaseUrl);

public sealed record LoginCommand(
    string Email,
    string Password,
    bool RememberMe);

public enum LoginStatus
{
    Succeeded = 0,
    InvalidCredentials = 1,
    EmailNotConfirmed = 2,
    PasswordNotSetForSocialAccount = 3,
    LockedOut = 4,
    AccountInactive = 5
}

public sealed record LoginResult(
    LoginStatus Status,
    AccountType? AccountType = null,
    string? Message = null);

public sealed record ForgotPasswordCommand(
    string Email,
    string ResetLinkBaseUrl);

public sealed record ResetPasswordCommand(
    string Email,
    string Code,
    string NewPassword);
