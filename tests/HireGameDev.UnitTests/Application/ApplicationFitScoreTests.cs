using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using Xunit;

namespace HireGameDev.UnitTests.Application;

public sealed class ApplicationFitScoreTests
{
    private static CandidateApplicationSnapshot Candidate(params ApplicationSkillSnapshot[] skills) => new(
        "Candidate", "Developer", default, "City", default, 3, null, default, true, false, null, null, null,
        skills, Array.Empty<ApplicationExperienceSnapshot>(), Array.Empty<ApplicationEducationSnapshot>(),
        Array.Empty<ApplicationPortfolioSnapshot>(), Array.Empty<ApplicationGameSnapshot>());
    private static ApplicationJobSkillSnapshot Requirement(string name) => new(name, SkillProficiency.Working);
    private static ApplicationSkillSnapshot Skill(string name, SkillProficiency level) => new(name, level, null, null, false);
    private static JobApplicationSnapshot Job(ApplicationJobSkillSnapshot[] required, ApplicationJobSkillSnapshot[] preferred,
        decimal? years = 2, JobWorkMode? mode = JobWorkMode.Remote, bool? sameCity = null) =>
        new("Job", "Studio", null, null, required, new(preferred, years, mode, sameCity));

    [Fact]
    public void ScoreUsesWeightedComparableCriteriaAndIsNotAnOutcomePrediction()
    {
        var fit = ApplicationFitScore.Create(Candidate(Skill("Unity", SkillProficiency.Strong), Skill("C#", SkillProficiency.Beginner)),
            Job(new[] { Requirement("Unity") }, new[] { Requirement("C#") }))!;
        Assert.Equal(85, fit.Percentage); // required 45 + preferred 0 + experience 25 + work 15
        Assert.Equal(100, fit.Coverage);
    }

    [Fact]
    public void MissingSkillsReduceCoverageInsteadOfBeingTreatedAsFailures()
    {
        var fit = ApplicationFitScore.Create(Candidate(Skill("Unity", SkillProficiency.Strong)),
            Job(new[] { Requirement("Unity"), Requirement("C#") }, Array.Empty<ApplicationJobSkillSnapshot>()))!;
        Assert.Equal(74, fit.Coverage); // 62.5 of 85 applicable weight
        Assert.Equal(100, fit.Percentage); // only assessed criteria; coverage is always shown
        Assert.Null(fit.Criteria.Single(x => x.Label == "Required: C#").Fraction);
    }

    [Fact]
    public void LowCoverageOrNoAssessedRequiredSkillsDoesNotGivePercentage()
    {
        var fit = ApplicationFitScore.Create(Candidate(), Job(new[] { Requirement("Unity") }, Array.Empty<ApplicationJobSkillSnapshot>()))!;
        Assert.Null(fit.Percentage);
        Assert.True(fit.Coverage < 70);
    }

    [Fact]
    public void DifferentLocationIsUnknownRatherThanAnAutomaticMismatch()
    {
        var fit = ApplicationFitScore.Create(Candidate(Skill("Unity", SkillProficiency.Strong)),
            Job(new[] { Requirement("Unity") }, Array.Empty<ApplicationJobSkillSnapshot>(), mode: JobWorkMode.Onsite, sameCity: false))!;
        Assert.Null(fit.Criteria.Single(x => x.Label == "Work preferences").Fraction);
    }

    [Fact]
    public void PreviousSnapshotsCannotInventRequirements()
    {
        Assert.Null(ApplicationFitScore.Create(Candidate(), new("Job", "Studio", null, null, Array.Empty<ApplicationJobSkillSnapshot>())));
    }
}
