using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Application.Features.Matching;

/// <summary>Shared weighted arithmetic. Unknown criteria reduce coverage, not the score.</summary>
internal static class MatchScoreMath
{
    internal static ApplicationFitScore Calculate(IReadOnlyList<FitCriterion> criteria)
    {
        var possible = criteria.Sum(x => x.Weight);
        var assessed = criteria.Where(x => x.Fraction.HasValue).Sum(x => x.Weight);
        var coverage = possible == 0 ? 0 : (int)Math.Round(100m * assessed / possible, MidpointRounding.AwayFromZero);
        int? score = assessed > 0
            ? (int)Math.Round(100m * criteria.Where(x => x.Fraction.HasValue).Sum(x => x.Weight * x.Fraction!.Value) / assessed, MidpointRounding.AwayFromZero)
            : null;
        return new ApplicationFitScore(score, coverage, criteria);
    }
}
