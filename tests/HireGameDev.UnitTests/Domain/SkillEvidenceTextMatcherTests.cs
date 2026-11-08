using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using Xunit;

namespace HireGameDev.UnitTests.Domain;

public sealed class SkillEvidenceTextMatcherTests
{
    [Fact]
    public void SkillEvidenceLevel_NumericValuesAndDisplayLabels_MatchSpecification()
    {
        Assert.Equal((short)0, (short)SkillEvidenceLevel.Claimed);
        Assert.Equal((short)1, (short)SkillEvidenceLevel.Backed);
        Assert.Equal((short)2, (short)SkillEvidenceLevel.Verified);

        Assert.Equal("Self-declared", SkillEvidenceLevel.Claimed.ToDisplayLabel());
        Assert.Equal("Supporting evidence submitted", SkillEvidenceLevel.Backed.ToDisplayLabel());
        Assert.Equal("Independently verified", SkillEvidenceLevel.Verified.ToDisplayLabel());
    }

    [Theory]
    [InlineData("Wrote custom memory allocators in C and Assembly.", "C")]
    [InlineData("Maintained low-level C/C++ physics solver.", "C")]
    [InlineData("Implemented native audio DSP (C, SIMD).", "C")]
    [InlineData("Engine plugins written in pure C.", "C")]
    [InlineData("Systems programming — C", "C")]
    [InlineData("Built core gameplay loops in C#.", "C#")]
    [InlineData("Used C#, Unity DOTS, and Addressables.", "C#")]
    [InlineData("Developed backend tooling with C#/.NET.", "C#")]
    [InlineData("Gameplay scripting (c#) for mobile title", "C#")]
    [InlineData("Shipped AA action title in Unreal Engine 5 and C++.", "C++")]
    [InlineData("Low-level C/C++ rendering pipeline", "C++")]
    [InlineData("Optimized frame time (C++) on console.", "C++")]
    [InlineData("Engine—C++ gameplay framework", "C++")]
    [InlineData("Built matchmaking and leaderboard services in Go.", "Go")]
    [InlineData("High-concurrency Go/gRPC game servers", "Go")]
    [InlineData("Backend stack: Go, Redis, PostgreSQL", "Go")]
    [InlineData("Dedicated server fleet (Go) on Agones", "Go")]
    [InlineData("LiveOps backend built on .NET 8.", ".NET")]
    [InlineData("Shared contracts across C#/.NET services", ".NET")]
    [InlineData("Built playable web prototypes with Three.js.", "Three.js")]
    [InlineData("Ported mobile RPG client to Cocos2d-x.", "Cocos2d-x")]
    [InlineData("Built multiplayer abilities with Unreal\n  Engine 5.", "Unreal Engine 5")]
    public void ContainsWholeSkillTerm_ValidWholeTokenMatches_ReturnsTrue(string description, string term)
    {
        Assert.True(SkillEvidenceTextMatcher.ContainsWholeSkillTerm(description, term));
    }

    [Theory]
    // "C" must NEVER false-match C#, C++, Objective-C, USB-C, C-suite, CSS, C4D, file extensions, contractions, or URLs
    [InlineData("Built gameplay systems in C# and Unity.", "C")]
    [InlineData("Engine rendering and SIMD math in C++.", "C")]
    [InlineData("Maintained legacy Objective-C iOS billing plugin.", "C")]
    [InlineData("Tested USB-C controller input latency.", "C")]
    [InlineData("Presented milestone roadmap to C-suite leadership.", "C")]
    [InlineData("Styled launcher UI using HTML and CSS.", "C")]
    [InlineData("Modeled hard-surface props in C4D.", "C")]
    [InlineData("Compiled C99 target without standalone token", "C")]
    [InlineData("Patched build script for main.c compilation", "C")]
    [InlineData("Localized dialogue lines ('c'est la vie').", "C")]
    [InlineData("Docs hosted at https://studio.example.com/c/docs.", "C")]
    // "C#" must NEVER false-match C, C++, F#, C##, AC#, or C#Sharp
    [InlineData("Wrote engine code in C and C++.", "C#")]
    [InlineData("Implemented analytics pipeline in F#.", "C#")]
    [InlineData("Internal tag C##1234 in tracker.", "C#")]
    [InlineData("Internal code AC# in build log.", "C#")]
    [InlineData("Custom wrapper C#SharpHelper.", "C#")]
    // "C++" must NEVER false-match C, C#, C+++, or C++Builder
    [InlineData("Gameplay scripting in C# and Lua.", "C++")]
    [InlineData("Typo token C+++ in scratch notes.", "C++")]
    [InlineData("Legacy C++Builder form migration.", "C++")]
    // "Go" must NEVER false-match Godot, Google, Cargo, MongoDB, go-to, on-the-go, CS:GO, go.dev, std::go, or URLs
    [InlineData("Shipped 2D roguelike in Godot 4.", "Go")]
    [InlineData("Integrated Google Play Games Services and Firebase.", "Go")]
    [InlineData("Built Rust tooling with Cargo workspaces.", "Go")]
    [InlineData("Managed player inventory in MongoDB.", "Go")]
    [InlineData("Served as the go-to engineer for shader debugging.", "Go")]
    [InlineData("Designed bite-sized sessions for on-the-go play.", "Go")]
    [InlineData("Organized community CS:GO tournaments.", "Go")]
    [InlineData("Referenced package docs on go.dev.", "Go")]
    [InlineData("Wrapped internal namespace std::go helper.", "Go")]
    [InlineData("Source repository: https://github.com/studio/go/tree/main", "Go")]
    // ".NET" must NEVER false-match domain suffixes like Battle.net
    [InlineData("Integrated Battle.net authentication SDK.", ".NET")]
    [InlineData("Published studio landing page at https://hiregamedev.net.", ".NET")]
    public void ContainsWholeSkillTerm_FalsePositiveSubstringsAndBoundaries_ReturnsFalse(string description, string term)
    {
        Assert.False(SkillEvidenceTextMatcher.ContainsWholeSkillTerm(description, term));
    }

    [Fact]
    public void ContainsAnySkillTerm_MatchesConfiguredAliasesAccurately()
    {
        var cppAliases = new[] { "CPP", "Modern C++", "C++17", "C++20" };
        var goAliases = new[] { "Golang" };
        var csharpAliases = new[] { "CSharp", ".NET", "DotNet" };

        Assert.True(SkillEvidenceTextMatcher.ContainsAnySkillTerm(
            "Optimized lock-free job queues using C++20 atomics.",
            "C++",
            cppAliases));

        Assert.True(SkillEvidenceTextMatcher.ContainsAnySkillTerm(
            "Built authoritative matchmaking backend in Golang.",
            "Go",
            goAliases));

        Assert.True(SkillEvidenceTextMatcher.ContainsAnySkillTerm(
            "Built editor pipelines on .NET.",
            "C#",
            csharpAliases));

        Assert.False(SkillEvidenceTextMatcher.ContainsAnySkillTerm(
            "Shipped mobile puzzle game in Godot with Battle.net link.",
            "Go",
            goAliases));
    }
}
