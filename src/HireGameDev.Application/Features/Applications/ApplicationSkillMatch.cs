using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Applications;

// A transparent comparison of self-reported skills, not a hiring recommendation or overall fit score.
public sealed record SkillMatchItem(string Name, SkillProficiency Required, SkillProficiency? Candidate);
public sealed record ApplicationSkillMatch(IReadOnlyList<SkillMatchItem> Skills)
{
    public int Met => Skills.Count(x => x.Candidate.HasValue && (int)x.Candidate.Value >= (int)x.Required);
    public int Total => Skills.Count;

    public static ApplicationSkillMatch? Create(CandidateApplicationSnapshot? candidate, JobApplicationSnapshot? job)
    {
        if (candidate?.Skills is null || job?.RequiredSkills is null || job.RequiredSkills.Count == 0 ||
            job.RequiredSkills.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || !Enum.IsDefined(x.MinProficiency)) ||
            candidate.Skills.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || !Enum.IsDefined(x.Proficiency))) return null;
        return new ApplicationSkillMatch(job.RequiredSkills.Select(required =>
        {
            var level = candidate.Skills.Where(x => string.Equals(x.Name.Trim(), required.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(x => (SkillProficiency?)x.Proficiency).OrderByDescending(x => x).FirstOrDefault();
            return new SkillMatchItem(required.Name, required.MinProficiency, level);
        }).ToArray());
    }
}
