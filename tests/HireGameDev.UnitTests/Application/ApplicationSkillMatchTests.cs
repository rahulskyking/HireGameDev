using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using Xunit;

namespace HireGameDev.UnitTests.Application;

public sealed class ApplicationSkillMatchTests
{
    private static CandidateApplicationSnapshot Candidate(params ApplicationSkillSnapshot[] skills) => new(
        "Candidate", "Developer", default, "City", default, 3, null, default,
        true, false, null, null, null, skills, Array.Empty<ApplicationExperienceSnapshot>(),
        Array.Empty<ApplicationEducationSnapshot>(), Array.Empty<ApplicationPortfolioSnapshot>(), Array.Empty<ApplicationGameSnapshot>());

    [Fact]
    public void ComparisonShowsOnlyRequiredSkillsAndRespectsMinimumLevel()
    {
        var candidate = Candidate(new("Unity", SkillProficiency.Strong, null, null, true),
            new("C#", SkillProficiency.Beginner, null, null, false));
        var job = new JobApplicationSnapshot("Developer", "Studio", null, null, new[] {
            new ApplicationJobSkillSnapshot("unity", SkillProficiency.Working),
            new ApplicationJobSkillSnapshot("C#", SkillProficiency.Strong),
            new ApplicationJobSkillSnapshot("Blender", SkillProficiency.Beginner) });
        var match = ApplicationSkillMatch.Create(candidate, job)!;
        Assert.Equal(1, match.Met);
        Assert.Equal(3, match.Total);
        Assert.Equal(SkillProficiency.Beginner, match.Skills[1].Candidate);
        Assert.Null(match.Skills[2].Candidate);
    }

    [Fact]
    public void NoRequirementsOrOldSnapshotsDoNotProduceAnInventedScore()
    {
        Assert.Null(ApplicationSkillMatch.Create(Candidate(), new JobApplicationSnapshot("Job", "Studio", null, null)));
        Assert.Null(ApplicationSkillMatch.Create(Candidate(), new JobApplicationSnapshot("Job", "Studio", null, null, Array.Empty<ApplicationJobSkillSnapshot>())));
    }
}
