namespace HireGameDev.Application.Features.Matching;

/// <summary>
/// An immutable policy value. Store and activate a version only after validation; changing
/// weights requires a new version. This type does not read settings or grant access.
/// </summary>
public sealed record MatchPolicy(string Version, decimal RequiredSkills, decimal PreferredSkills,
    decimal Experience, decimal RelevantWork, decimal WorkPreferences, decimal Salary)
{
    public static MatchPolicy Initial { get; } = new("2-proposed", 40m, 10m, 20m, 15m, 10m, 5m);

    public bool IsValid => !string.IsNullOrWhiteSpace(Version) && Version.Length <= 80 &&
        new[] { RequiredSkills, PreferredSkills, Experience, RelevantWork, WorkPreferences, Salary }
            .All(x => x is >= 0m and <= 100m) &&
        RequiredSkills + PreferredSkills + Experience + RelevantWork + WorkPreferences + Salary == 100m;
}
