using HireGameDev.Domain.Enums;
using HireGameDev.Application.Features.Matching;

namespace HireGameDev.Application.Features.Applications;

// Versioned, deterministic comparison, not a probability of success or automated decision.
public sealed record FitCriterion(string Label, string Explanation, decimal Weight, decimal? Fraction);
public sealed record ApplicationFitScore(int? Percentage, int Coverage, IReadOnlyList<FitCriterion> Criteria)
{
    public const string Version = "1";
    public static ApplicationFitScore? Create(CandidateApplicationSnapshot? candidate, JobApplicationSnapshot? job)
    {
        var inputs = job?.MatchInputs;
        if (candidate?.Skills is null || job?.RequiredSkills is null || inputs?.PreferredSkills is null || inputs.AlgorithmVersion != 1 ||
            candidate.Skills.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || !Enum.IsDefined(x.Proficiency)) ||
            job.RequiredSkills.Concat(inputs.PreferredSkills).Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || !Enum.IsDefined(x.MinProficiency))) return null;
        var criteria = new List<FitCriterion>();
        AddSkills(criteria, "Required", 45m, job.RequiredSkills, candidate.Skills);
        AddSkills(criteria, "Preferred", 15m, inputs.PreferredSkills, candidate.Skills);
        if (inputs.MinimumYears is >= 0 && candidate.YearsExperience >= 0)
        {
            bool meets = candidate.YearsExperience >= inputs.MinimumYears.Value;
            criteria.Add(new("Experience", meets ? "Meets the minimum years" : "Below the minimum years", 25m, meets ? 1m : 0m));
        }
        if (inputs.WorkMode.HasValue && Enum.IsDefined(inputs.WorkMode.Value))
        {
            decimal? result = inputs.WorkMode == JobWorkMode.Remote ? (candidate.OpenToRemote ? 1m : 0m) : inputs.SameCity == true ? 1m : null;
            var text = inputs.WorkMode == JobWorkMode.Remote
                ? (candidate.OpenToRemote ? "Open to remote work" : "Remote work not selected")
                : inputs.SameCity == true ? "Candidate and job are in the same city" : "Location needs a conversation";
            criteria.Add(new("Work preferences", text, 15m, result));
        }
        return MatchScoreMath.Calculate(criteria);
    }

    private static void AddSkills(List<FitCriterion> items, string group, decimal weight,
        IReadOnlyList<ApplicationJobSkillSnapshot> required, IReadOnlyList<ApplicationSkillSnapshot> skills)
    {
        if (required.Count == 0) return; // The dimension does not apply to this job.
        foreach (var skill in required)
        {
            var level = skills.Where(x => string.Equals(x.Name.Trim(), skill.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(x => (SkillProficiency?)x.Proficiency).OrderByDescending(x => x).FirstOrDefault();
            bool meets = level.HasValue && (int)level.Value >= (int)skill.MinProficiency;
            items.Add(new($"{group}: {skill.Name}", !level.HasValue ? $"Not listed · requested {skill.MinProficiency}" :
                $"Listed {level} · requested {skill.MinProficiency}", weight / required.Count, level.HasValue ? (meets ? 1m : 0m) : null));
        }
    }
}
