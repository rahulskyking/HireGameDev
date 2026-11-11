using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class StudioVerificationRulesTests
{
    [Theory]
    [InlineData("gmail.com", true)]
    [InlineData("GMAIL.COM", true)]
    [InlineData("outlook.in", true)]
    [InlineData("rediffmail.com", true)]
    [InlineData("proton.me", true)]
    [InlineData("noddingheadsgames.com", false)]
    [InlineData("supergaming.com", false)]
    public void IsFreeEmailDomain_ClassifiesPersonalAndCustomDomains(string domain, bool expectedFree)
    {
        Assert.Equal(expectedFree, StudioVerificationRules.IsFreeEmailDomain(domain));
    }

    [Fact]
    public void AssessEmailDomain_DetectsExactAndSubdomainMatchesAndMismatches()
    {
        var exactMatch = StudioVerificationRules.AssessEmailDomain(
            "hr@noddingheadsgames.com",
            "https://www.noddingheadsgames.com");
        Assert.Equal(StudioEmailDomainSignal.DomainMatched, exactMatch.Signal);
        Assert.Equal("noddingheadsgames.com", exactMatch.OwnerEmailDomain);
        Assert.Equal("noddingheadsgames.com", exactMatch.WebsiteDomain);

        var subdomainMatch = StudioVerificationRules.AssessEmailDomain(
            "talent@careers.supergaming.com",
            "https://supergaming.com");
        Assert.Equal(StudioEmailDomainSignal.DomainMatched, subdomainMatch.Signal);

        var parentWebsiteMatch = StudioVerificationRules.AssessEmailDomain(
            "founder@supergaming.com",
            "https://jobs.supergaming.com");
        Assert.Equal(StudioEmailDomainSignal.DomainMatched, parentWebsiteMatch.Signal);

        var unmatchedCompany = StudioVerificationRules.AssessEmailDomain(
            "founder@supergaming.in",
            "https://supergaming.com");
        Assert.Equal(StudioEmailDomainSignal.CompanyDomainUnmatched, unmatchedCompany.Signal);

        var companyNoWebsite = StudioVerificationRules.AssessEmailDomain(
            "founder@indiestudio.in",
            null);
        Assert.Equal(StudioEmailDomainSignal.CompanyDomainNoWebsite, companyNoWebsite.Signal);

        var freeEmail = StudioVerificationRules.AssessEmailDomain(
            "indie.founder@gmail.com",
            "https://indiestudio.itch.io");
        Assert.Equal(StudioEmailDomainSignal.PersonalEmail, freeEmail.Signal);
        Assert.True(freeEmail.IsFreeMail);
    }

    [Fact]
    public void TryNormalizeProofUrls_EnforcesHttpsCapAndDeduplication()
    {
        var ok = StudioVerificationRules.TryNormalizeProofUrls(
            new[]
            {
                "  https://store.steampowered.com/app/730390/Raji_An_Ancient_Epic/ ",
                "https://www.linkedin.com/company/nodding-heads-games/",
                "https://www.linkedin.com/company/nodding-heads-games/",
                ""
            },
            out var normalized,
            out var error);

        Assert.True(ok, error);
        Assert.Equal(2, normalized.Length);

        var httpRejected = StudioVerificationRules.TryNormalizeProofUrls(
            new[] { "http://insecure.example.com" },
            out _,
            out var httpErr);
        Assert.False(httpRejected);
        Assert.NotNull(httpErr);

        var overCapRejected = StudioVerificationRules.TryNormalizeProofUrls(
            new[]
            {
                "https://example.com/1",
                "https://example.com/2",
                "https://example.com/3",
                "https://example.com/4",
                "https://example.com/5",
                "https://example.com/6"
            },
            out _,
            out var capErr);
        Assert.False(overCapRejected);
        Assert.Contains("5", capErr, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateReadiness_RequiresCoreProfileAndAtLeastOnePublicPresenceSignal()
    {
        var validDescription = new string('A', StudioProfileRules.MinOnboardingDescriptionLength);

        var missingPresence = StudioVerificationRules.EvaluateReadiness(
            studioName: "Indie Pixel Co",
            description: validDescription,
            cityId: 1,
            engineSkillCount: 1,
            normalizedWebsiteUrl: null,
            normalizedGstin: null,
            shippedTitleStoreUrlCount: 0,
            proofUrlCount: 0);
        Assert.False(missingPresence.CanSubmit);

        var readyViaProofUrlOnly = StudioVerificationRules.EvaluateReadiness(
            studioName: "Indie Pixel Co",
            description: validDescription,
            cityId: 1,
            engineSkillCount: 1,
            normalizedWebsiteUrl: null,
            normalizedGstin: null,
            shippedTitleStoreUrlCount: 0,
            proofUrlCount: 1);
        Assert.True(readyViaProofUrlOnly.CanSubmit);

        var readyViaStoreUrlOnly = StudioVerificationRules.EvaluateReadiness(
            studioName: "Indie Pixel Co",
            description: validDescription,
            cityId: 1,
            engineSkillCount: 1,
            normalizedWebsiteUrl: null,
            normalizedGstin: null,
            shippedTitleStoreUrlCount: 1,
            proofUrlCount: 0);
        Assert.True(readyViaStoreUrlOnly.CanSubmit);

        var missingEngine = StudioVerificationRules.EvaluateReadiness(
            studioName: "Indie Pixel Co",
            description: validDescription,
            cityId: 1,
            engineSkillCount: 0,
            normalizedWebsiteUrl: "https://indiepixel.in",
            normalizedGstin: null,
            shippedTitleStoreUrlCount: 0,
            proofUrlCount: 0);
        Assert.False(missingEngine.CanSubmit);
    }

    [Fact]
    public void TryValidateIdentityChangeRequest_RequiresActualChangeAndValidFormats()
    {
        var noChange = StudioVerificationRules.TryValidateIdentityChangeRequest(
            currentName: "Monsoon Pixel",
            currentSlug: "monsoon-pixel",
            currentGstin: null,
            rawProposedName: "  Monsoon Pixel ",
            rawProposedSlug: "monsoon-pixel",
            rawProposedGstin: "",
            out _,
            out _,
            out _,
            out var noChangeErr);
        Assert.False(noChange);
        Assert.NotNull(noChangeErr);

        var validIncorporation = StudioVerificationRules.TryValidateIdentityChangeRequest(
            currentName: "Monsoon Pixel",
            currentSlug: "monsoon-pixel",
            currentGstin: null,
            rawProposedName: "Monsoon Pixel Studios Pvt Ltd",
            rawProposedSlug: "monsoon-pixel-studios",
            rawProposedGstin: "29abcde1234f1z5",
            out var normalizedName,
            out var normalizedSlug,
            out var normalizedGstin,
            out var validErr);
        Assert.True(validIncorporation, validErr);
        Assert.Equal("Monsoon Pixel Studios Pvt Ltd", normalizedName);
        Assert.Equal("monsoon-pixel-studios", normalizedSlug);
        Assert.Equal("29ABCDE1234F1Z5", normalizedGstin);
    }
}
