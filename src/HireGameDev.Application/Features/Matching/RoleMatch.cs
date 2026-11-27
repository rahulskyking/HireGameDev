using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Matching;

// Inputs must be assembled from authorized, privacy-filtered data. Neither entitlement,
// activity, name, phone, current salary nor profile completeness enters this model.
public sealed record MatchSkill(int SkillId, string Name, SkillProficiency Proficiency, SkillEvidenceLevel Evidence);
public sealed record MatchRequirement(int SkillId, string Name, SkillProficiency Minimum, bool Required);
public sealed record RoleMatchCandidate(IReadOnlyList<MatchSkill> Skills, decimal? YearsExperience,
    bool? OpenToRemote, bool? SameCity, decimal? ExpectedAnnualLpa);
public sealed record RoleMatchJob(IReadOnlyList<MatchRequirement> Skills, decimal? MinimumYears,
    JobWorkMode? WorkMode, decimal? MaximumAnnualLpa);
public sealed record RoleMatchResult(string PolicyVersion, ApplicationFitScore Score);

/// <summary>
/// Proposed next-version calculation, not yet connected to discovery or stored snapshots.
/// Relevant-work matching is deliberately unknown until an authorized structured input exists.
/// Callers must normalize pay to annual INR lakhs and recheck all visibility before scoring.
/// </summary>
public static class RoleMatch
{
    public static RoleMatchResult? Calculate(RoleMatchCandidate? candidate, RoleMatchJob? job, MatchPolicy? policy)
    {
        if (candidate?.Skills is null || job?.Skills is null || policy is null || !policy.IsValid ||
            candidate.Skills.Any(s => s is null || s.SkillId <= 0 || string.IsNullOrWhiteSpace(s.Name) || !Enum.IsDefined(s.Proficiency) || !Enum.IsDefined(s.Evidence)) ||
            job.Skills.Any(s => s is null || s.SkillId <= 0 || string.IsNullOrWhiteSpace(s.Name) || !Enum.IsDefined(s.Minimum)) ||
            candidate.Skills.GroupBy(s => s.SkillId).Any(g => g.Count() != 1) ||
            job.Skills.GroupBy(s => s.SkillId).Any(g => g.Count() != 1) ||
            candidate.YearsExperience is < 0 or > 50 || job.MinimumYears is < 0 or > 50 ||
            candidate.ExpectedAnnualLpa is <= 0 || job.MaximumAnnualLpa is <= 0 ||
            (job.WorkMode.HasValue && !Enum.IsDefined(job.WorkMode.Value))) return null;

        var criteria = new List<FitCriterion>();
        AddSkills(criteria, candidate.Skills, job.Skills.Where(s => s.Required).ToArray(), policy.RequiredSkills, "Required");
        AddSkills(criteria, candidate.Skills, job.Skills.Where(s => !s.Required).ToArray(), policy.PreferredSkills, "Preferred");

        if (job.MinimumYears.HasValue && policy.Experience > 0)
        {
            decimal? fraction = candidate.YearsExperience.HasValue ? (candidate.YearsExperience >= job.MinimumYears ? 1m : 0m) : null;
            criteria.Add(new("Experience", fraction is null ? "Experience not available" : fraction == 1m ? "Meets the minimum years" : "Below the minimum years", policy.Experience, fraction));
        }
        if (policy.RelevantWork > 0 && job.Skills.Count > 0)
            criteria.Add(new("Relevant work", "Relevant work has not been assessed", policy.RelevantWork, null));
        if (job.WorkMode.HasValue && policy.WorkPreferences > 0)
        {
            decimal? fraction = job.WorkMode == JobWorkMode.Remote
                ? candidate.OpenToRemote.HasValue ? (candidate.OpenToRemote.Value ? 1m : 0m) : null
                : candidate.SameCity == true ? 1m : null;
            var explanation = job.WorkMode == JobWorkMode.Remote
                ? fraction is null ? "Remote preference not available" : fraction == 1m ? "Open to remote work" : "Remote work not selected"
                : fraction == 1m ? "Candidate and job are in the same city" : "Location needs a conversation";
            criteria.Add(new("Work preferences", explanation, policy.WorkPreferences, fraction));
        }
        if (job.MaximumAnnualLpa.HasValue && policy.Salary > 0)
        {
            decimal? fraction = candidate.ExpectedAnnualLpa.HasValue ? (candidate.ExpectedAnnualLpa <= job.MaximumAnnualLpa ? 1m : 0m) : null;
            criteria.Add(new("Salary", fraction is null ? "Salary preference not available" : fraction == 1m ? "Expected pay is within the advertised maximum" : "Expected pay is above the advertised maximum", policy.Salary, fraction));
        }
        return new RoleMatchResult(policy.Version, MatchScoreMath.Calculate(criteria));
    }

    private static void AddSkills(List<FitCriterion> criteria, IReadOnlyList<MatchSkill> candidate,
        IReadOnlyList<MatchRequirement> requirements, decimal groupWeight, string label)
    {
        if (requirements.Count == 0 || groupWeight == 0) return;
        foreach (var requirement in requirements)
        {
            var skill = candidate.FirstOrDefault(s => s.SkillId == requirement.SkillId);
            decimal? fraction = skill is null ? null : (int)skill.Proficiency >= (int)requirement.Minimum ? 1m : 0m;
            var evidence = skill?.Evidence switch
            {
                SkillEvidenceLevel.Verified => "verified evidence",
                SkillEvidenceLevel.Backed => "supporting evidence",
                _ => "self-declared"
            };
            var explanation = skill is null ? $"Not listed · requested {requirement.Minimum}"
                : $"Listed {skill.Proficiency} ({evidence}) · requested {requirement.Minimum}";
            criteria.Add(new FitCriterion($"{label}: {requirement.Name}", explanation, groupWeight / requirements.Count, fraction));
        }
    }
}
