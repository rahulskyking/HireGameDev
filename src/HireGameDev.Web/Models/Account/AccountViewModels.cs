using System.ComponentModel.DataAnnotations;
using HireGameDev.Application.Features.Auth;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Web.Models.Account;

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Choose how you want to use HireGameDev.")]
    public AccountType AccountType { get; init; } = AccountType.Candidate;

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 80 characters.")]
    [Display(Name = "Full name or founder name")]
    public string DisplayName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; init; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; init; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm that you are 18 years or older.")]
    [Display(Name = "I confirm that I am 18 years of age or older")]
    public bool ConfirmAge18Plus { get; init; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the Terms of Use and Privacy Notice.")]
    [Display(Name = "I agree to the Terms of Use and Privacy Notice")]
    public bool AcceptTermsAndPrivacy { get; init; }

    public string? ReturnUrl { get; init; }

    public IReadOnlyList<ExternalProviderDescriptor> ExternalProviders { get; set; } = [];
}

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; init; } = string.Empty;

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; init; }

    public string? ReturnUrl { get; init; }

    public bool ShowResendConfirmation { get; set; }

    public bool ShowSetPasswordPrompt { get; set; }

    public IReadOnlyList<ExternalProviderDescriptor> ExternalProviders { get; set; } = [];
}

public sealed class CompleteExternalRegistrationViewModel
{
    public string ProviderDisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool AllowedForStudios { get; set; } = true;

    [Required(ErrorMessage = "Choose how you want to use HireGameDev.")]
    public AccountType AccountType { get; set; } = AccountType.Candidate;

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 80 characters.")]
    [Display(Name = "Full name or founder name")]
    public string DisplayName { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm that you are 18 years or older.")]
    [Display(Name = "I confirm that I am 18 years of age or older")]
    public bool ConfirmAge18Plus { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the Terms of Use and Privacy Notice.")]
    [Display(Name = "I agree to the Terms of Use and Privacy Notice")]
    public bool AcceptTermsAndPrivacy { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string Email { get; init; } = string.Empty;

    public bool LinkSent { get; set; }
}

public sealed class ResetPasswordViewModel
{
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Code { get; init; } = string.Empty;

    public string Mode { get; init; } = "reset";

    [Required(ErrorMessage = "New password is required.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your new password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed class ConfirmEmailViewModel
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;
}
