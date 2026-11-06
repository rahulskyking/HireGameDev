using FluentValidation;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

internal sealed class SaveCandidateStep2BackgroundCommandValidator : AbstractValidator<SaveCandidateStep2BackgroundCommand>
{
    private static readonly DateOnly MinStartDate = new(1970, 1, 1);
    private static readonly DateOnly MaxAllowedDate = new(2100, 12, 31);

    public SaveCandidateStep2BackgroundCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.YearsExperience)
            .InclusiveBetween(CandidateProfileRules.MinYearsExperience, CandidateProfileRules.MaxYearsExperience)
            .WithMessage("Years of experience must be between 0 and 50.")
            .Must(CandidateProfileRules.IsHalfYearIncrement)
            .WithMessage("Years of experience must use 0.5-year increments (e.g., 0, 0.5, 1, 1.5).");

        RuleFor(x => x)
            .Must(x => CandidateProfileRules.IsSeniorityWithinAllowedBand(x.YearsExperience, x.Seniority))
            .WithMessage(x =>
            {
                var suggested = CandidateProfileRules.SuggestSeniorityFromYears(x.YearsExperience);
                return $"Selected seniority must be within ±1 level of '{suggested}' for {x.YearsExperience:0.#} years of experience.";
            });

        RuleFor(x => x.Experiences)
            .NotNull()
            .Must(list => list.Count <= CandidateProfileRules.MaxCandidateExperiences)
            .WithMessage($"You can add at most {CandidateProfileRules.MaxCandidateExperiences} work experience entries.");

        RuleForEach(x => x.Experiences).ChildRules(exp =>
        {
            exp.RuleFor(e => e.CompanyName)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length is >= 1 and <= 160)
                .WithMessage("Each work experience entry requires a company or studio name (up to 160 characters).");

            exp.RuleFor(e => e.Title)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length is >= 1 and <= 160)
                .WithMessage("Each work experience entry requires a role title (up to 160 characters).");

            exp.RuleFor(e => e.StartDate)
                .Must(d => d >= MinStartDate && d <= MaxAllowedDate)
                .WithMessage("Work experience start date must be between 1970 and 2100.");

            exp.RuleFor(e => e)
                .Must(e => !(e.IsCurrent && e.EndDate.HasValue))
                .WithMessage("Clear the end date when 'I currently work here' is selected.");

            exp.RuleFor(e => e)
                .Must(e => !e.EndDate.HasValue || (e.EndDate.Value >= e.StartDate && e.EndDate.Value <= MaxAllowedDate))
                .WithMessage("Work experience end date cannot be earlier than the start date.");

            exp.RuleFor(e => e.Description)
                .Must(d => string.IsNullOrEmpty(d) || d.Trim().Length <= CandidateProfileRules.MaxExperienceDescriptionLength)
                .WithMessage($"Work experience description must be {CandidateProfileRules.MaxExperienceDescriptionLength} characters or fewer.");
        });

        RuleFor(x => x.Education)
            .NotNull()
            .Must(list => list.Count <= CandidateProfileRules.MaxCandidateEducationEntries)
            .WithMessage($"You can add at most {CandidateProfileRules.MaxCandidateEducationEntries} education entries.");

        RuleForEach(x => x.Education).ChildRules(edu =>
        {
            edu.RuleFor(e => e.Institution)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length is >= 1 and <= 200)
                .WithMessage("Each education entry requires an institution or university name (up to 200 characters).");

            edu.RuleFor(e => e.Degree)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length is >= 1 and <= 160)
                .WithMessage("Each education entry requires a degree or diploma title (up to 160 characters).");

            edu.RuleFor(e => e.Field)
                .Must(f => string.IsNullOrEmpty(f) || f.Trim().Length <= 160)
                .WithMessage("Field of study must be 160 characters or fewer.");

            edu.RuleFor(e => e.StartYear)
                .Must(y => !y.HasValue || (y.Value >= CandidateProfileRules.MinEducationYear && y.Value <= CandidateProfileRules.MaxEducationYear))
                .WithMessage($"Education start year must be between {CandidateProfileRules.MinEducationYear} and {CandidateProfileRules.MaxEducationYear}.");

            edu.RuleFor(e => e.EndYear)
                .Must(y => !y.HasValue || (y.Value >= CandidateProfileRules.MinEducationYear && y.Value <= CandidateProfileRules.MaxEducationYear))
                .WithMessage($"Education completion year must be between {CandidateProfileRules.MinEducationYear} and {CandidateProfileRules.MaxEducationYear}.");

            edu.RuleFor(e => e)
                .Must(e => !e.StartYear.HasValue || !e.EndYear.HasValue || e.EndYear.Value >= e.StartYear.Value)
                .WithMessage("Education completion year cannot be earlier than the start year.");
        });
    }
}
