using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence.Converters;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class StudioProfileRulesTests
{
    [Fact]
    public void StudioSizeBandConverter_RoundTripsExactBlueprintCheckStrings()
    {
        var converter = new StudioSizeBandValueConverter();
        var toDb = converter.ConvertToProviderExpression.Compile();
        var fromDb = converter.ConvertFromProviderExpression.Compile();

        Assert.Equal("1-10", toDb(StudioSizeBand.Band1To10));
        Assert.Equal("11-50", toDb(StudioSizeBand.Band11To50));
        Assert.Equal("51-200", toDb(StudioSizeBand.Band51To200));
        Assert.Equal("200+", toDb(StudioSizeBand.Band200Plus));

        Assert.Equal(StudioSizeBand.Band1To10, fromDb("1-10"));
        Assert.Equal(StudioSizeBand.Band11To50, fromDb("11-50"));
        Assert.Equal(StudioSizeBand.Band51To200, fromDb("51-200"));
        Assert.Equal(StudioSizeBand.Band200Plus, fromDb("200+"));
    }

    [Fact]
    public void StudioSnakeCaseEnums_MatchBlueprintCheckConstraints()
    {
        Assert.Equal("indie", SnakeCaseEnumValueConverter<StudioType>.ToSnakeCaseValue(StudioType.Indie));
        Assert.Equal("game_studio", SnakeCaseEnumValueConverter<StudioType>.ToSnakeCaseValue(StudioType.GameStudio));
        Assert.Equal("outsourcing", SnakeCaseEnumValueConverter<StudioType>.ToSnakeCaseValue(StudioType.Outsourcing));
        Assert.Equal("publisher", SnakeCaseEnumValueConverter<StudioType>.ToSnakeCaseValue(StudioType.Publisher));
        Assert.Equal("other", SnakeCaseEnumValueConverter<StudioType>.ToSnakeCaseValue(StudioType.Other));

        Assert.Equal("unverified", SnakeCaseEnumValueConverter<StudioVerificationStatus>.ToSnakeCaseValue(StudioVerificationStatus.Unverified));
        Assert.Equal("pending", SnakeCaseEnumValueConverter<StudioVerificationStatus>.ToSnakeCaseValue(StudioVerificationStatus.Pending));
        Assert.Equal("verified", SnakeCaseEnumValueConverter<StudioVerificationStatus>.ToSnakeCaseValue(StudioVerificationStatus.Verified));
        Assert.Equal("rejected", SnakeCaseEnumValueConverter<StudioVerificationStatus>.ToSnakeCaseValue(StudioVerificationStatus.Rejected));

        Assert.Equal("owner", SnakeCaseEnumValueConverter<StudioMemberRole>.ToSnakeCaseValue(StudioMemberRole.Owner));
        Assert.Equal("recruiter", SnakeCaseEnumValueConverter<StudioMemberRole>.ToSnakeCaseValue(StudioMemberRole.Recruiter));
        Assert.Equal("viewer", SnakeCaseEnumValueConverter<StudioMemberRole>.ToSnakeCaseValue(StudioMemberRole.Viewer));
    }

    [Theory]
    [InlineData("Nodding Heads Games Pvt. Ltd.", "nodding-heads-games-pvt-ltd")]
    [InlineData("   Fox & Hound Interactive  ", "fox-and-hound-interactive")]
    [InlineData("X", "x-studio")]
    [InlineData("Admin", "admin-studio")]
    [InlineData("Jobs", "jobs-studio")]
    public void GenerateSlugFromName_ProducesValidNonReservedUrlSlug(string rawName, string expectedSlug)
    {
        var generated = StudioProfileRules.GenerateSlugFromName(rawName);

        Assert.Equal(expectedSlug, generated);
        Assert.True(StudioProfileRules.TryNormalizeSlug(generated, out var normalized, out var err), err);
        Assert.Equal(expectedSlug, normalized);
    }

    [Theory]
    [InlineData("nodding-heads-games", true)]
    [InlineData("  SuperGaming-IN  ", true)]
    [InlineData("a", false)]
    [InlineData("-leading-hyphen", false)]
    [InlineData("trailing-hyphen-", false)]
    [InlineData("double--hyphen", false)]
    [InlineData("has_underscore", false)]
    [InlineData("has/slash", false)]
    [InlineData("admin", false)]
    [InlineData("studios", false)]
    [InlineData("signin-google", false)]
    public void TryNormalizeSlug_EnforcesFormatBoundsAndReservedList(string rawSlug, bool expectedValid)
    {
        var ok = StudioProfileRules.TryNormalizeSlug(rawSlug, out var normalized, out var error);

        Assert.Equal(expectedValid, ok);
        if (expectedValid)
        {
            Assert.Equal(rawSlug.Trim().ToLowerInvariant(), normalized);
            Assert.Null(error);
        }
        else
        {
            Assert.Empty(normalized);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void TryNormalizeGstin_AllowsBlankAndValidatesIndianGstinFormat()
    {
        Assert.True(StudioProfileRules.TryNormalizeGstin(null, out var nullResult, out _));
        Assert.Null(nullResult);

        Assert.True(StudioProfileRules.TryNormalizeGstin("   ", out var blankResult, out _));
        Assert.Null(blankResult);

        Assert.True(StudioProfileRules.TryNormalizeGstin(" 29abcde1234f1z5 ", out var validGstin, out var validErr), validErr);
        Assert.Equal("29ABCDE1234F1Z5", validGstin);

        // Entity number at index 12 cannot be '0' per ^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$
        Assert.False(StudioProfileRules.TryNormalizeGstin("29ABCDE1234F0Z5", out _, out _));

        // Character at index 13 must be 'Z'
        Assert.False(StudioProfileRules.TryNormalizeGstin("29ABCDE1234F1X5", out _, out _));

        // Wrong length
        Assert.False(StudioProfileRules.TryNormalizeGstin("29ABCDE1234F1Z", out _, out _));
    }

    [Fact]
    public void TryNormalizeCultureTags_EnforcesAllowlistDeduplicationAndSixTagCap()
    {
        var ok = StudioProfileRules.TryNormalizeCultureTags(
            new[] { "remote_friendly", "NO_CRUNCH", "remote_friendly", "original_ip" },
            out var normalized,
            out var err);

        Assert.True(ok, err);
        Assert.Equal(new[] { "remote_friendly", "no_crunch", "original_ip" }, normalized);

        var unknownOk = StudioProfileRules.TryNormalizeCultureTags(new[] { "free_pizza" }, out _, out var unknownErr);
        Assert.False(unknownOk);
        Assert.Contains("Unsupported culture tag", unknownErr, StringComparison.OrdinalIgnoreCase);

        var sevenTags = new[]
        {
            "remote_friendly", "hybrid_work", "flexible_hours",
            "no_crunch", "indie_spirit", "original_ip", "health_insurance"
        };
        var tooManyOk = StudioProfileRules.TryNormalizeCultureTags(sevenTags, out _, out var capErr);
        Assert.False(tooManyOk);
        Assert.Contains("6", capErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://www.noddingheadsgames.com/about", "noddingheadsgames.com")]
    [InlineData("https://supergaming.com/", "supergaming.com")]
    [InlineData(null, null)]
    public void ExtractCompanyDomainFromWebsite_StripsWwwAndLowercasesHost(string? url, string? expectedDomain)
    {
        Assert.Equal(expectedDomain, StudioProfileRules.ExtractCompanyDomainFromWebsite(url));
    }

    [Fact]
    public void SlugLockAndPublicVisibilityRules_EnforceAdr018Invariants()
    {
        Assert.True(StudioProfileRules.CanEditSlug(StudioMemberRole.Owner, StudioVerificationStatus.Unverified));
        Assert.True(StudioProfileRules.CanEditSlug(StudioMemberRole.Owner, StudioVerificationStatus.Rejected));
        Assert.False(StudioProfileRules.CanEditSlug(StudioMemberRole.Owner, StudioVerificationStatus.Pending));
        Assert.False(StudioProfileRules.CanEditSlug(StudioMemberRole.Owner, StudioVerificationStatus.Verified));
        Assert.False(StudioProfileRules.CanEditSlug(StudioMemberRole.Recruiter, StudioVerificationStatus.Unverified));

        // Anonymous visitors can only view verified, non-deleted studios.
        Assert.False(StudioProfileRules.CanViewPublicStudioPage(StudioVerificationStatus.Unverified, isDeleted: false, isMemberOfStudio: false, isAdmin: false));
        Assert.False(StudioProfileRules.CanViewPublicStudioPage(StudioVerificationStatus.Pending, isDeleted: false, isMemberOfStudio: false, isAdmin: false));
        Assert.True(StudioProfileRules.CanViewPublicStudioPage(StudioVerificationStatus.Verified, isDeleted: false, isMemberOfStudio: false, isAdmin: false));

        // Studio members and Admins can preview unverified/pending studios, unless soft-deleted.
        Assert.True(StudioProfileRules.CanViewPublicStudioPage(StudioVerificationStatus.Unverified, isDeleted: false, isMemberOfStudio: true, isAdmin: false));
        Assert.True(StudioProfileRules.CanViewPublicStudioPage(StudioVerificationStatus.Pending, isDeleted: false, isMemberOfStudio: false, isAdmin: true));
        Assert.False(StudioProfileRules.CanViewPublicStudioPage(StudioVerificationStatus.Verified, isDeleted: true, isMemberOfStudio: true, isAdmin: true));
    }
}
