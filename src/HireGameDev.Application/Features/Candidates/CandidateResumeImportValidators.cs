using FluentValidation;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

internal sealed class ApplyCandidateResumeDraftCommandValidator : AbstractValidator<ApplyCandidateResumeDraftCommand>
{
    public ApplyCandidateResumeDraftCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        When(x => x.ApplyHeadline, () =>
        {
            RuleFor(x => x.Headline)
                .Must(h => !string.IsNullOrWhiteSpace(h) &&
                           h.Trim().Length >= 3 &&
                           h.Trim().Length <= 80)
                .WithMessage("Headline must be between 3 and 80 characters.");
        });

        When(x => x.ApplyYearsExperience, () =>
        {
            RuleFor(x => x.YearsExperience)
                .NotNull()
                .WithMessage("Years of experience is required when selected for import.")
                .Must(y => y.HasValue && CandidateProfileRules.IsHalfYearIncrement(y.Value))
                .WithMessage("Years of experience must be between 0 and 50 in 0.5-year increments.");
        });

        RuleFor(x => x.Skills)
            .NotNull()
            .Must(s => s.Count <= CandidateProfileRules.MaxCandidateSkills)
            .WithMessage($"You can import at most {CandidateProfileRules.MaxCandidateSkills} skills at once.");

        RuleForEach(x => x.Skills).ChildRules(skill =>
        {
            skill.RuleFor(s => s.SkillId).GreaterThan(0);
            skill.RuleFor(s => s.Proficiency).IsInEnum();
            skill.RuleFor(s => s.YearsUsed)
                .Must(y => !y.HasValue || (y.Value >= CandidateProfileRules.MinYearsExperience && y.Value <= CandidateProfileRules.MaxYearsExperience))
                .WithMessage("Skill years used must be between 0 and 50.");
            skill.RuleFor(s => s.LastUsedYear)
                .Must(y => !y.HasValue || (y.Value >= CandidateProfileRules.MinSkillLastUsedYear && y.Value <= CandidateProfileRules.MaxSkillLastUsedYear))
                .WithMessage($"Skill last used year must be between {CandidateProfileRules.MinSkillLastUsedYear} and {CandidateProfileRules.MaxSkillLastUsedYear}.");
        });

        RuleFor(x => x.Experiences)
            .NotNull()
            .Must(e => e.Count <= CandidateProfileRules.MaxCandidateExperiences)
            .WithMessage($"You can import at most {CandidateProfileRules.MaxCandidateExperiences} work experience entries.");

        RuleForEach(x => x.Experiences).ChildRules(exp =>
        {
            exp.RuleFor(e => e.CompanyName)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 160)
                .WithMessage("Experience company name is required (max 160 characters).");

            exp.RuleFor(e => e.Title)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 160)
                .WithMessage("Experience job title is required (max 160 characters).");

            exp.RuleFor(e => e)
                .Must(e => e.IsCurrent || !e.EndDate.HasValue || e.EndDate.Value >= e.StartDate)
                .WithMessage("Experience end date must be on or after the start date.");

            exp.RuleFor(e => e.Description)
                .Must(d => d is null || d.Trim().Length <= CandidateProfileRules.MaxExperienceDescriptionLength)
                .WithMessage($"Experience description cannot exceed {CandidateProfileRules.MaxExperienceDescriptionLength} characters.");
        });

        RuleFor(x => x.Education)
            .NotNull()
            .Must(e => e.Count <= CandidateProfileRules.MaxCandidateEducationEntries)
            .WithMessage($"You can import at most {CandidateProfileRules.MaxCandidateEducationEntries} education entries.");

        RuleForEach(x => x.Education).ChildRules(edu =>
        {
            edu.RuleFor(e => e.Institution)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 200)
                .WithMessage("Education institution is required (max 200 characters).");

            edu.RuleFor(e => e.Degree)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 160)
                .WithMessage("Education degree is required (max 160 characters).");

            edu.RuleFor(e => e.Field)
                .Must(v => v is null || v.Trim().Length <= 160)
                .WithMessage("Education field of study cannot exceed 160 characters.");

            edu.RuleFor(e => e)
                .Must(e =>
                    (!e.StartYear.HasValue || (e.StartYear.Value >= CandidateProfileRules.MinEducationYear && e.StartYear.Value <= CandidateProfileRules.MaxEducationYear)) &&
                    (!e.EndYear.HasValue || (e.EndYear.Value >= CandidateProfileRules.MinEducationYear && e.EndYear.Value <= CandidateProfileRules.MaxEducationYear)) &&
                    (!e.StartYear.HasValue || !e.EndYear.HasValue || e.EndYear.Value >= e.StartYear.Value))
                .WithMessage("Education years must be valid and end year cannot precede start year.");
        });

        RuleFor(x => x.ShippedTitles)
            .NotNull()
            .Must(t => t.Count <= CandidateProfileRules.MaxShippedTitles)
            .WithMessage($"You can import at most {CandidateProfileRules.MaxShippedTitles} shipped titles.");

        RuleForEach(x => x.ShippedTitles).ChildRules(title =>
        {
            title.RuleFor(t => t.GameName)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 160)
                .WithMessage("Shipped title game name is required (max 160 characters).");

            title.RuleFor(t => t.StudioName)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 160)
                .WithMessage("Shipped title studio name is required (max 160 characters).");

            title.RuleFor(t => t.Role)
                .Must(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 120)
                .WithMessage("Shipped title role is required (max 120 characters).");

            title.RuleFor(t => t.Platforms)
                .NotNull()
                .Must(p => p.Count <= 5 && p.All(CandidateProfileRules.IsValidGamePlatform))
                .WithMessage("Selected game platforms are invalid.");

            title.RuleFor(t => t.ReleaseYear)
                .Must(y => !y.HasValue || (y.Value >= CandidateProfileRules.MinShippedTitleReleaseYear && y.Value <= CandidateProfileRules.MaxShippedTitleReleaseYear))
                .WithMessage($"Shipped title release year must be between {CandidateProfileRules.MinShippedTitleReleaseYear} and {CandidateProfileRules.MaxShippedTitleReleaseYear}.");
        });
    }
}
