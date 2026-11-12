using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class CandidateProfileRulesTests
{
    [Fact]
    public void CalculateCompleteness_SkipsDeferredPhoneVerificationForNextBestStep_AndReaches90WithoutOtp()
    {
        var breakdown = CandidateProfileRules.CalculateCompleteness(new CandidateCompletenessInput(
            HasPhoto: true,
            HasHeadline: true,
            HasBio: true,
            HasCity: true,
            SkillCount: 5,
            ExperienceCount: 1,
            EducationCount: 1,
            ShippedTitleCount: 1,
            PortfolioLinkCount: 0,
            HasExpectedCtcAndNoticePeriod: true,
            IsPhoneVerified: false,
            YearsExperience: 3.5m,
            Seniority: SeniorityLevel.Mid));

        Assert.Equal(90, breakdown.TotalScore);
        Assert.True(breakdown.MeetsApplyThreshold);
        Assert.True(breakdown.MeetsDiscoverabilityThreshold);
        Assert.Null(breakdown.NextBestStep);

        var phoneItem = Assert.Single(breakdown.Items, i => i.Key == "phone_verified");
        Assert.False(phoneItem.IsComplete);
        Assert.True(phoneItem.IsDeferred);
        Assert.Equal(10, phoneItem.Points);
    }

    [Fact]
    public void CalculateCompleteness_PicksPhotoAfter70Points_SkippingDeferredPhoneOtp()
    {
        var breakdown = CandidateProfileRules.CalculateCompleteness(new CandidateCompletenessInput(
            HasPhoto: false,
            HasHeadline: true,
            HasBio: true,
            HasCity: true,
            SkillCount: 5,
            ExperienceCount: 1,
            EducationCount: 0,
            ShippedTitleCount: 0,
            PortfolioLinkCount: 1,
            HasExpectedCtcAndNoticePeriod: true,
            IsPhoneVerified: false,
            YearsExperience: 2.0m,
            Seniority: SeniorityLevel.Junior));

        // Proof (20) + Skills (15) + Experience (15) + Bio (10) + Preferences (10) + Headline (5) + City (5) = 80.
        Assert.Equal(80, breakdown.TotalScore);
        Assert.NotNull(breakdown.NextBestStep);
        Assert.Equal("photo", breakdown.NextBestStep!.Key);
    }

    [Fact]
    public void CalculateCompleteness_FresherGetsExperienceCreditFromEducation_AndReaches100WhenPhoneVerified()
    {
        var breakdown = CandidateProfileRules.CalculateCompleteness(new CandidateCompletenessInput(
            HasPhoto: true,
            HasHeadline: true,
            HasBio: true,
            HasCity: true,
            SkillCount: 5,
            ExperienceCount: 0,
            EducationCount: 1,
            ShippedTitleCount: 1,
            PortfolioLinkCount: 1,
            HasExpectedCtcAndNoticePeriod: true,
            IsPhoneVerified: true,
            YearsExperience: 0.0m,
            Seniority: SeniorityLevel.Fresher));

        Assert.Equal(100, breakdown.TotalScore);
        Assert.True(breakdown.MeetsApplyThreshold);
        Assert.True(breakdown.MeetsDiscoverabilityThreshold);
        Assert.Null(breakdown.NextBestStep);

        var phoneItem = Assert.Single(breakdown.Items, i => i.Key == "phone_verified");
        Assert.True(phoneItem.IsComplete);
        Assert.False(phoneItem.IsDeferred);
    }

    [Fact]
    public void StudioVisibilityHelpers_EnforceThresholdsAndCompensationPrivacy()
    {
        Assert.False(CandidateProfileRules.IsDiscoverableByStudios(isDiscoverable: false, completeness: 90));
        Assert.False(CandidateProfileRules.IsDiscoverableByStudios(isDiscoverable: true, completeness: 69));
        Assert.True(CandidateProfileRules.IsDiscoverableByStudios(isDiscoverable: true, completeness: 70));

        Assert.False(CandidateProfileRules.ShouldDisplayCurrentCtcToStudio(currentCtcLpa: null, showCurrentCtc: true));
        Assert.False(CandidateProfileRules.ShouldDisplayCurrentCtcToStudio(currentCtcLpa: 18.5m, showCurrentCtc: false));
        Assert.True(CandidateProfileRules.ShouldDisplayCurrentCtcToStudio(currentCtcLpa: 18.5m, showCurrentCtc: true));

        Assert.False(CandidateProfileRules.IsPhoneVisibleToStudio());
        Assert.True(CandidateProfileRules.IsPhoneVisibleToStudio(isShortlisted: true));
    }
}
