using FluentValidation;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Auth;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.AccountType)
            .Must(type => type is AccountType.Candidate or AccountType.Studio)
            .WithMessage("Please choose whether you are joining as a Candidate or a Studio.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Name is required.")
            .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
            .MaximumLength(80).WithMessage("Name must be 80 characters or fewer.");

        // Per ADR-008: Any valid email address (including Gmail/Outlook or company domain) is allowed at registration.
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email address is required.")
            .MaximumLength(254)
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .MaximumLength(128)
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one number.");

        RuleFor(x => x.ConfirmAge18Plus)
            .Equal(true)
            .WithMessage("You must confirm that you are 18 years of age or older.");

        RuleFor(x => x.AcceptTermsAndPrivacy)
            .Equal(true)
            .WithMessage("You must accept the Terms of Use and Privacy Notice.");
    }
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email address is required.")
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email address is required.")
            .EmailAddress().WithMessage("Enter a valid email address.");
    }
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email address is required.")
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Invalid or missing password reset token.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .MaximumLength(128)
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one number.");
    }
}

public sealed class CompleteExternalRegistrationCommandValidator : AbstractValidator<CompleteExternalRegistrationCommand>
{
    public CompleteExternalRegistrationCommandValidator()
    {
        RuleFor(x => x.AccountType)
            .Must(type => type is AccountType.Candidate or AccountType.Studio)
            .WithMessage("Please choose whether you are joining as a Candidate or a Studio.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Name is required.")
            .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
            .MaximumLength(80).WithMessage("Name must be 80 characters or fewer.");

        RuleFor(x => x.ConfirmAge18Plus)
            .Equal(true)
            .WithMessage("You must confirm that you are 18 years of age or older.");

        RuleFor(x => x.AcceptTermsAndPrivacy)
            .Equal(true)
            .WithMessage("You must accept the Terms of Use and Privacy Notice.");
    }
}
