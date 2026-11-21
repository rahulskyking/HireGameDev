using HireGameDev.Domain.Entities;

namespace HireGameDev.Application.Features.Applications;

/// <summary>
/// Builds only the approved display-safe values from fully loaded entities. This is not an
/// authorization or consent check. The caller must load all collections and coordinate edits
/// with submission; never accept this object from a client as proof of consent.
/// </summary>
public static class ApplicationSnapshotBuilder
{
    public const int CurrentVersion = 1;
    public const string ConfidentialProject = "Confidential Project (Under NDA)";
    public const string ConfidentialStudio = "Confidential Studio";

    public static ApplicationSnapshotDraft Create(CandidateProfile candidate, Job job, StudioProfile studio, string? screeningAnswer)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(studio);
        if (job.StudioId != studio.Id) throw new ArgumentException("The job does not belong to this studio.", nameof(studio));
        if (candidate.City is null) throw new InvalidOperationException("Candidate city must be loaded before building a snapshot.");
        if (string.IsNullOrWhiteSpace(job.Title)) throw new InvalidOperationException("A job title is required for the snapshot.");
        if (string.IsNullOrWhiteSpace(job.ScreeningQuestion) && !string.IsNullOrWhiteSpace(screeningAnswer))
            throw new ArgumentException("An answer requires a screening question.", nameof(screeningAnswer));

        var skills = candidate.Skills.OrderBy(x => x.SkillId).Select(x => new ApplicationSkillSnapshot(
            x.Skill?.Name ?? throw new InvalidOperationException("Candidate skill must be loaded."),
            x.Proficiency, x.YearsUsed, x.LastUsedYear, x.IsPrimary))
            .ToArray();
        var experience = candidate.Experiences.OrderBy(x => x.Id).Select(x => new ApplicationExperienceSnapshot(
            x.CompanyName, x.Title, x.StartDate, x.EndDate, x.IsCurrent, x.Description))
            .ToArray();
        var education = candidate.Education.OrderBy(x => x.Id).Select(x => new ApplicationEducationSnapshot(
            x.Institution, x.Degree, x.Field, x.StartYear, x.EndYear))
            .ToArray();
        var portfolio = candidate.PortfolioLinks.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new ApplicationPortfolioSnapshot(x.LinkType, x.Url, x.Title,
                x.EvidencedSkills.Select(s => s.Skill?.Name ?? throw new InvalidOperationException("Portfolio skill must be loaded."))
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray())).ToArray();
        var games = candidate.ShippedTitles.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new ApplicationGameSnapshot(
                x.IsNda ? ConfidentialProject : x.GameName,
                x.IsNda && x.HideStudioInPublic ? ConfidentialStudio : x.StudioName,
                x.Role, x.Platforms.ToArray(),
                x.EngineSkillId.HasValue ? x.EngineSkill?.Name ?? throw new InvalidOperationException("Game engine must be loaded.") : null,
                x.ReleaseYear, x.IsNda ? null : x.StoreUrl)).ToArray();
        var profile = new CandidateApplicationSnapshot(
            candidate.FullName, candidate.Headline, candidate.PrimaryCategory, candidate.City.Name,
            candidate.Seniority, candidate.YearsExperience, candidate.Bio, candidate.Availability,
            candidate.OpenToRemote, candidate.WillingToRelocate, candidate.NoticePeriodDays,
            candidate.ExpectedCtcLpa, candidate.ShowCurrentCtc ? candidate.CurrentCtcLpa : null,
            skills, experience, education, portfolio, games);
        return new ApplicationSnapshotDraft(CurrentVersion, profile,
            new JobApplicationSnapshot(job.Title, studio.Name, job.ScreeningQuestion, screeningAnswer), candidate.Phone);
    }
}
