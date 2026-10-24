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

public sealed record ExternalProviderDescriptor(
    string Scheme,
    string DisplayName,
    bool AllowedForStudios);

public enum ExternalCallbackStatus
{
    SignedIn = 0,
    RequiresAccountCompletion = 1,
    ExternalInfoMissing = 2,
    RemoteError = 3,
    EmailMissingOrUnverified = 4,
    ProviderNotAllowedForAccountType = 5,
    AccountInactive = 6,
    LockedOut = 7
}

public sealed record ExternalLoginCallbackResult(
    ExternalCallbackStatus Status,
    AccountType? AccountType = null,
    string? Email = null,
    string? ProviderDisplayName = null,
    string? Message = null);

public sealed record PendingExternalRegistration(
    string Provider,
    string ProviderDisplayName,
    string Email,
    string SuggestedDisplayName,
    bool AllowedForStudios);

public sealed record CompleteExternalRegistrationCommand(
    AccountType AccountType,
    string DisplayName,
    bool ConfirmAge18Plus,
    bool AcceptTermsAndPrivacy,
    string SetPasswordLinkBaseUrl);

public sealed record CompleteExternalRegistrationResult(
    Guid UserId,
    string Email,
    AccountType AccountType);
