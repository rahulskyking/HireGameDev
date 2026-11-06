using FluentValidation;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Features.Candidates;

internal sealed class SaveCandidateSkillsCommandValidator : AbstractValidator<SaveCandidateSkillsCommand>
{
    public SaveCandidateSkillsCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.Skills)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Select at least 3 skills.")
            .Must(list => list.Count >= CandidateProfileRules.MinOnboardingSkills)
            .WithMessage($"Select at least {CandidateProfileRules.MinOnboardingSkills} skills to continue.")
            .Must(list => list.Count <= CandidateProfileRules.MaxCandidateSkills)
            .WithMessage($"You can select at most {CandidateProfileRules.MaxCandidateSkills} skills.")
            .Must(HaveDistinctSkillIds)
            .WithMessage("Duplicate skills are not allowed.")
            .Must(list => list.Count(s => s.IsPrimary) <= CandidateProfileRules.MaxPrimarySkills)
            .WithMessage($"Mark at most {CandidateProfileRules.MaxPrimarySkills} skills as primary.");

        RuleForEach(x => x.Skills).ChildRules(skill =>
        {
            skill.RuleFor(s => s.SkillId)
                .GreaterThan(0).WithMessage("Invalid skill selected.");

            skill.RuleFor(s => s.Proficiency)
                .IsInEnum().WithMessage("Proficiency must be Beginner (1), Working (2), Strong (3), or Expert (4).");

            skill.RuleFor(s => s.YearsUsed)
                .Must(v => v is null || (v.Value >= 0m && v.Value <= CandidateProfileRules.MaxYearsExperience && CandidateProfileRules.IsHalfYearIncrement(v.Value)))
                .WithMessage("Years used for each skill must be between 0 and 50 in 0.5-year increments.");

            skill.RuleFor(s => s.LastUsedYear)
                .Must(v => v is null || (v.Value >= CandidateProfileRules.MinSkillLastUsedYear && v.Value <= CandidateProfileRules.MaxSkillLastUsedYear))
                .WithMessage($"Last used year must be between {CandidateProfileRules.MinSkillLastUsedYear} and {CandidateProfileRules.MaxSkillLastUsedYear}.");
        });
    }

    private static bool HaveDistinctSkillIds(IReadOnlyList<CandidateSkillSelectionInput> skills)
    {
        var seen = new HashSet<int>(skills.Count);
        for (var i = 0; i < skills.Count; i++)
        {
            if (!seen.Add(skills[i].SkillId))
            {
                return false;
            }
        }

        return true;
    }
}
