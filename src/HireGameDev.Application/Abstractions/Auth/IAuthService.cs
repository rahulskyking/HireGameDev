using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Auth;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Abstractions.Auth;

public interface IAuthService
{
    Task<Result<RegisterResult>> RegisterAsync(
        RegisterCommand command,
        CancellationToken cancellationToken = default);

    Task<Result> ConfirmEmailAsync(
        ConfirmEmailCommand command,
        CancellationToken cancellationToken = default);

    Task<Result> ResendConfirmationAsync(
        ResendConfirmationCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<LoginResult>> LoginAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);

    Task<Result> SendPasswordResetOrSetupLinkAsync(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalProviderDescriptor>> GetConfiguredExternalProvidersAsync(
        CancellationToken cancellationToken = default);

    Task<Result> ValidateExternalChallengeAsync(
        string provider,
        AccountType? preferredAccountType = null,
        CancellationToken cancellationToken = default);

    Task<ExternalLoginCallbackResult> HandleExternalLoginCallbackAsync(
        string? remoteError,
        CancellationToken cancellationToken = default);

    Task<Result<PendingExternalRegistration>> GetPendingExternalRegistrationAsync(
        CancellationToken cancellationToken = default);

    Task<Result<CompleteExternalRegistrationResult>> CompleteExternalRegistrationAsync(
        CompleteExternalRegistrationCommand command,
        CancellationToken cancellationToken = default);
}
