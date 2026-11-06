using FluentValidation;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

internal sealed class SaveCandidateBasicsCommandValidator : AbstractValidator<SaveCandidateBasicsCommand>
{
    public SaveCandidateBasicsCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.FullName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Full name is required.")
            .Must(v => v.Trim().Length is >= 2 and <= 80)
            .WithMessage("Full name must be between 2 and 80 characters.");

        RuleFor(x => x.Headline)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Headline is required.")
            .Must(v => v.Trim().Length is >= 3 and <= 80)
            .WithMessage("Headline must be between 3 and 80 characters.");

        RuleFor(x => x.PrimaryCategory)
            .IsInEnum().WithMessage("Select a valid primary discipline.");

        RuleFor(x => x.CityId)
            .GreaterThan(0).WithMessage("Select your primary city from the list.");

        RuleFor(x => x.Bio)
            .Must(v => v is null || v.Trim().Length <= 1500)
            .WithMessage("Bio must be 1500 characters or fewer.");
    }
}

internal sealed class SaveCandidateExperienceCoreCommandValidator : AbstractValidator<SaveCandidateExperienceCoreCommand>
{
    public SaveCandidateExperienceCoreCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.YearsExperience)
            .InclusiveBetween(
                CandidateProfileRules.MinYearsExperience,
                CandidateProfileRules.MaxYearsExperience)
            .WithMessage("Years of experience must be between 0 and 50.")
            .Must(CandidateProfileRules.IsHalfYearIncrement)
            .WithMessage("Years of experience must use 0.5-year increments (e.g. 0, 1.5, 4).");

        RuleFor(x => x.Seniority)
            .IsInEnum().WithMessage("Select a valid seniority level.");

        RuleFor(x => x)
            .Must(cmd => CandidateProfileRules.IsSeniorityWithinAllowedBand(cmd.YearsExperience, cmd.Seniority))
            .WithMessage(cmd =>
            {
                var suggested = CandidateProfileRules.SuggestSeniorityFromYears(cmd.YearsExperience);
                return $"For {cmd.YearsExperience:0.#} years of experience, suggested seniority is '{suggested}' and may only be adjusted by at most 1 level.";
            });
    }
}

internal sealed class SaveCandidatePreferencesCommandValidator : AbstractValidator<SaveCandidatePreferencesCommand>
{
    public SaveCandidatePreferencesCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.ExpectedCtcLpa)
            .GreaterThan(0m).WithMessage("Expected CTC (₹ LPA) must be greater than 0.")
            .LessThanOrEqualTo(500m).WithMessage("Expected CTC (₹ LPA) must be 500 LPA or less.");

        RuleFor(x => x.CurrentCtcLpa)
            .Must(v => v is null || (v.Value >= 0m && v.Value <= 500m))
            .WithMessage("Current CTC (₹ LPA) must be between 0 and 500 LPA.");

        RuleFor(x => x.NoticePeriodDays)
            .Must(CandidateProfileRules.IsValidNoticePeriod)
            .WithMessage("Notice period must be 0, 15, 30, 45, 60, or 90 days.");

        RuleFor(x => x.Availability)
            .IsInEnum().WithMessage("Select a valid availability status.");
    }
}
