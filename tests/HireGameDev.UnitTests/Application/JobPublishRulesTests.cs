using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Enums;
using Xunit;

namespace HireGameDev.UnitTests.Application;

public sealed class JobPublishRulesTests
{
    internal static JobContent Valid() => new()
    {
        Title = "Unity Gameplay Developer", Category = DisciplineCategory.Programming, JobType = JobType.FullTime,
        SeniorityMin = SeniorityLevel.Junior, SeniorityMax = SeniorityLevel.Mid, MinYears = 1, MaxYears = 4,
        Description = "Build gameplay systems in Unity and C#. Work with designers and artists to implement features, profile performance, review changes, and write automated tests. You will maintain clear technical documentation and collaborate with a small experienced game development team.",
        WorkMode = JobWorkMode.Remote, PayUnit = JobPayUnit.Lpa, SalaryMin = 10, SalaryMax = 15,
        Skills = new[] { new JobSkillInput(1, true, SkillProficiency.Working), new JobSkillInput(2, true, SkillProficiency.Strong), new JobSkillInput(3, false, SkillProficiency.Working) }
    };

    [Fact]
    public void ValidJob_AndExactSalaryBoundary_PassWithoutWarnings()
    {
        var result = JobPublishRules.Evaluate(Valid());
        Assert.True(result.CanPublish);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void IncompleteDraft_ProducesFieldErrors_WithoutMutatingContent()
    {
        var content = new JobContent();
        var result = JobPublishRules.Evaluate(content);
        Assert.False(result.CanPublish);
        Assert.Contains(result.Findings, x => x.Field == "Title");
        Assert.Contains(result.Findings, x => x.Field == "SalaryMin");
        Assert.Contains(result.Findings, x => x.Code == "Jobs.SkillCount");
        Assert.Null(content.Title);
    }

    [Theory]
    [InlineData(3, 40, 1.5, false)]
    [InlineData(10, 15, 1.5, true)]
    [InlineData(10, 16, 1.5, false)]
    [InlineData(10, 16, 2, true)]
    public void SalarySpread_IsConfigurable(double min, double max, double spread, bool expected) =>
        Assert.Equal(expected, JobPublishRules.Evaluate(Valid() with { SalaryMin = (decimal)min, SalaryMax = (decimal)max }, (decimal)spread).CanPublish);

    [Theory]
    [InlineData("UNITY GAMEPLAY DEVELOPER", "Jobs.AllCapsTitle")]
    [InlineData("Unity Developer 15 LPA", "Jobs.SalaryInTitle")]
    [InlineData("Unity Developer ₹15 lakh", "Jobs.SalaryInTitle")]
    [InlineData("Unity Developer 🚀🚀🚀", "Jobs.EmojiSpam")]
    [InlineData("Contact jobs@example.com", "Jobs.ContactDetails")]
    [InlineData("Call +91 98765 43210", "Jobs.ContactDetails")]
    public void TitlePolicy_BlocksInvalidContent(string title, string code)
    {
        var result = JobPublishRules.Evaluate(Valid() with { Title = title });
        Assert.False(result.CanPublish);
        Assert.Contains(result.Findings, x => x.Code == code && x.Field == "Title");
    }

    [Theory]
    [InlineData("Email jobs@example.com", "Jobs.ContactDetails")]
    [InlineData("Email jobs [at] example [dot] com", "Jobs.ContactDetails")]
    [InlineData("Call +91 98765 43210", "Jobs.ContactDetails")]
    [InlineData("Apply at https://example.com/apply", "Jobs.ExternalLink")]
    [InlineData("Apply at careers.example.com", "Jobs.ExternalLink")]
    [InlineData("Apply at example.in/apply", "Jobs.ExternalLink")]
    [InlineData("[Apply here](/external-apply)", "Jobs.ExternalLink")]
    [InlineData("<a href='apply'>Apply</a>", "Jobs.ExternalLink")]
    [InlineData("Apply h\u200Bttps://example.com/apply", "Jobs.ExternalLink")]
    public void DescriptionPolicy_BlocksContactAndApplicationLinks(string suffix, string code)
    {
        var result = JobPublishRules.Evaluate(Valid() with { Description = Valid().Description + " " + suffix });
        Assert.False(result.CanPublish);
        Assert.Contains(result.Findings, x => x.Code == code && x.Field == "Description");
    }

    [Fact]
    public void TechnicalTokensDatesAndProseCompensation_AreNotMisclassified()
    {
        var result = JobPublishRules.Evaluate(Valid() with
        {
            Description = Valid().Description + " Experience with C, C++, C#, Go, ASP.NET, Node.js, Three.js and Socket.IO is welcome. We shipped titles in 2021–2024 and offer 10–15 LPA."
        });
        Assert.True(result.CanPublish);
    }

    [Fact]
    public void ScreeningQuestion_CannotBypassContactPolicy()
    {
        var result = JobPublishRules.Evaluate(Valid() with { ScreeningQuestion = "Send your answer to jobs@example.com" });
        Assert.Contains(result.Findings, x => x.Code == "Jobs.ContactDetails" && x.Field == "ScreeningQuestion");
    }

    [Fact]
    public void SkillCountsRequiredFlagDuplicatesAndProficiency_AreBlocking()
    {
        Assert.False(JobPublishRules.Evaluate(Valid() with { Skills = Array.Empty<JobSkillInput>() }).CanPublish);
        Assert.Contains(JobPublishRules.Evaluate(Valid() with { Skills = Valid().Skills.Select(x => x with { IsRequired = false }).ToArray() }).Findings, x => x.Code == "Jobs.RequiredSkill");
        Assert.Contains(JobPublishRules.Evaluate(Valid() with { Skills = Enumerable.Repeat(new JobSkillInput(1, true, (SkillProficiency)9), 3).ToArray() }).Findings, x => x.Code == "Jobs.SkillProficiency");
        Assert.Contains(JobPublishRules.Evaluate(Valid() with { Skills = Enumerable.Repeat(Valid().Skills[0], 3).ToArray() }).Findings, x => x.Code == "Jobs.DistinctSkills");
    }

    [Fact]
    public void RemoteAndOnsiteCityRules_AndPayUnitRules_AreEnforced()
    {
        Assert.False(JobPublishRules.Evaluate(Valid() with { CityId = 1 }).CanPublish);
        Assert.False(JobPublishRules.Evaluate(Valid() with { WorkMode = JobWorkMode.Onsite }).CanPublish);
        Assert.True(JobPublishRules.Evaluate(Valid() with { WorkMode = JobWorkMode.Hybrid, CityId = 1 }).CanPublish);
        Assert.False(JobPublishRules.Evaluate(Valid() with { PayUnit = JobPayUnit.MonthlyInr }).CanPublish);
        Assert.True(JobPublishRules.Evaluate(Valid() with { PayUnit = JobPayUnit.MonthlyInr, JobType = JobType.Internship }).CanPublish);
    }

    [Fact]
    public void InvalidRangesAndEnums_AreBlocked()
    {
        var result = JobPublishRules.Evaluate(Valid() with
        {
            Category = (DisciplineCategory)99, SalaryMin = -1, SalaryMax = 0, MinYears = 8, MaxYears = 2,
            SeniorityMin = SeniorityLevel.Lead, SeniorityMax = SeniorityLevel.Fresher, MaxNoticeDays = 8, Openings = 51
        });
        Assert.False(result.CanPublish);
        foreach (var code in new[] { "Jobs.CategoryRequired", "Jobs.SalaryMin", "Jobs.SalaryMax", "Jobs.ExperienceRange", "Jobs.SeniorityRange", "Jobs.NoticePeriod", "Jobs.Openings" })
            Assert.Contains(result.Findings, x => x.Code == code);
    }

    [Fact]
    public void QualityWarnings_DoNotBlockAndDoNotRewrite()
    {
        var content = Valid() with { FresherFriendly = true, MinYears = 2, SeniorityMax = SeniorityLevel.Fresher, SeniorityMin = SeniorityLevel.Fresher };
        var result = JobPublishRules.Evaluate(content);
        Assert.True(result.CanPublish);
        Assert.Contains(result.Findings, x => x.Code == "Jobs.FresherExperienceMismatch");
        Assert.Contains(result.Findings, x => x.Code == "Jobs.SeniorityExperienceMismatch");
        Assert.Equal((decimal?)2m, content.MinYears);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void InvalidPolicySetting_FailsClosed(int spread) => Assert.False(JobPublishRules.Evaluate(Valid(), spread).CanPublish);

    [Fact]
    public void Fingerprint_IgnoresCosmeticAndNonIdentityEdits()
    {
        var first = Valid();
        var second = first with { Title = "  UNITY   GAMEPLAY DEVELOPER ", SalaryMin = 11, Openings = 5, Skills = first.Skills.Reverse().ToArray() };
        Assert.Equal(JobContentIdentity.Fingerprint(first), JobContentIdentity.Fingerprint(second));
        Assert.NotEqual(JobContentIdentity.Fingerprint(first), JobContentIdentity.Fingerprint(first with { Description = first.Description + " Different role." }));
    }
}
