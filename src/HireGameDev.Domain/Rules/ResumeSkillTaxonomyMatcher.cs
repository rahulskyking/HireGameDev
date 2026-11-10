using System.Text;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Rules;

public sealed record ResumeCatalogSkillEntry(
    int SkillId,
    string Name,
    string Slug,
    SkillCategory Category,
    IReadOnlyList<string> Aliases);

public enum SkillTaxonomyMatchKind
{
    ExactNameOrAlias = 1,
    NormalizedToken = 2,
    TrigramSimilarity = 3
}

public sealed record ResumeSkillTaxonomyMatch(
    int SkillId,
    string SkillName,
    string SkillSlug,
    SkillCategory Category,
    SkillTaxonomyMatchKind MatchKind,
    double SimilarityScore);

/// <summary>
/// Matches LLM-extracted resume skill names against the canonical <c>skills</c> taxonomy using
/// exact <c>name</c>/<c>slug</c>/<c>aliases</c> lookup, punctuation-normalized token equivalence,
/// and <c>pg_trgm</c>-compatible padded character 3-gram similarity (Blueprint v2.0 Section 5 Step 5).
/// Unmatched skills are preserved by callers as review suggestions rather than silently dropped.
/// </summary>
public static class ResumeSkillTaxonomyMatcher
{
    public const double MinTrigramSimilarity = 0.55;
    private const int MinLengthForFuzzyTrigram = 4;

    public static ResumeSkillTaxonomyMatch? MatchSingle(
        string? rawSkillName,
        IReadOnlyList<ResumeCatalogSkillEntry> catalog)
    {
        if (string.IsNullOrWhiteSpace(rawSkillName) || catalog is null || catalog.Count == 0)
        {
            return null;
        }

        var trimmed = StripVersionNoiseSuffix(rawSkillName.Trim());
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Tier 1: Exact case-insensitive match against Name, Slug, or any Alias (first try raw trimmed, then version-stripped).
        var exactRaw = TryMatchExact(rawSkillName.Trim(), catalog) ?? TryMatchExact(trimmed, catalog);
        if (exactRaw is not null)
        {
            return exactRaw;
        }

        // Tier 2: Compact normalized token match (e.g. "NodeJS" -> "Node.js", "ThreeJS" -> "Three.js", "UnrealEngine" -> "Unreal Engine"),
        // strictly preserving '+' and '#' so C, C#, and C++ never collide.
        var normalizedQuery = NormalizeCompactKeepLanguageSymbols(trimmed);
        if (normalizedQuery.Length > 0)
        {
            for (var i = 0; i < catalog.Count; i++)
            {
                var entry = catalog[i];
                if (NormalizedEquals(normalizedQuery, entry.Name) ||
                    NormalizedEquals(normalizedQuery, entry.Slug))
                {
                    return new ResumeSkillTaxonomyMatch(
                        entry.SkillId,
                        entry.Name,
                        entry.Slug,
                        entry.Category,
                        SkillTaxonomyMatchKind.NormalizedToken,
                        1.0);
                }

                for (var a = 0; a < entry.Aliases.Count; a++)
                {
                    if (NormalizedEquals(normalizedQuery, entry.Aliases[a]))
                    {
                        return new ResumeSkillTaxonomyMatch(
                            entry.SkillId,
                            entry.Name,
                            entry.Slug,
                            entry.Category,
                            SkillTaxonomyMatchKind.NormalizedToken,
                            1.0);
                    }
                }
            }
        }

        // Tier 3: pg_trgm-equivalent padded 3-gram Jaccard similarity for multi-character terms (>= 4 chars)
        // that do not involve '#' or '+' symbol mismatches.
        if (normalizedQuery.Length < MinLengthForFuzzyTrigram ||
            ContainsLanguageSymbol(trimmed))
        {
            return null;
        }

        var queryTrigrams = BuildPgTrigrams(trimmed);
        if (queryTrigrams.Count == 0)
        {
            return null;
        }

        ResumeCatalogSkillEntry? bestEntry = null;
        var bestScore = 0.0;

        for (var i = 0; i < catalog.Count; i++)
        {
            var entry = catalog[i];
            EvaluateCandidateTerm(queryTrigrams, entry.Name, entry, ref bestEntry, ref bestScore);

            for (var a = 0; a < entry.Aliases.Count; a++)
            {
                EvaluateCandidateTerm(queryTrigrams, entry.Aliases[a], entry, ref bestEntry, ref bestScore);
            }
        }

        if (bestEntry is not null && bestScore >= MinTrigramSimilarity)
        {
            return new ResumeSkillTaxonomyMatch(
                bestEntry.SkillId,
                bestEntry.Name,
                bestEntry.Slug,
                bestEntry.Category,
                SkillTaxonomyMatchKind.TrigramSimilarity,
                Math.Round(bestScore, 3));
        }

        return null;
    }

    public static double ComputePgTrigramSimilarity(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0.0;
        }

        var leftSet = BuildPgTrigrams(left);
        var rightSet = BuildPgTrigrams(right);
        if (leftSet.Count == 0 || rightSet.Count == 0)
        {
            return 0.0;
        }

        return ComputeJaccard(leftSet, rightSet);
    }

    private static ResumeSkillTaxonomyMatch? TryMatchExact(
        string candidateText,
        IReadOnlyList<ResumeCatalogSkillEntry> catalog)
    {
        for (var i = 0; i < catalog.Count; i++)
        {
            var entry = catalog[i];
            if (string.Equals(entry.Name, candidateText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Slug, candidateText, StringComparison.OrdinalIgnoreCase))
            {
                return new ResumeSkillTaxonomyMatch(
                    entry.SkillId,
                    entry.Name,
                    entry.Slug,
                    entry.Category,
                    SkillTaxonomyMatchKind.ExactNameOrAlias,
                    1.0);
            }

            for (var a = 0; a < entry.Aliases.Count; a++)
            {
                if (string.Equals(entry.Aliases[a], candidateText, StringComparison.OrdinalIgnoreCase))
                {
                    return new ResumeSkillTaxonomyMatch(
                        entry.SkillId,
                        entry.Name,
                        entry.Slug,
                        entry.Category,
                        SkillTaxonomyMatchKind.ExactNameOrAlias,
                        1.0);
                }
            }
        }

        return null;
    }

    private static void EvaluateCandidateTerm(
        HashSet<string> queryTrigrams,
        string candidateTerm,
        ResumeCatalogSkillEntry ownerEntry,
        ref ResumeCatalogSkillEntry? bestEntry,
        ref double bestScore)
    {
        if (string.IsNullOrWhiteSpace(candidateTerm))
        {
            return;
        }

        var normalizedCandidate = NormalizeCompactKeepLanguageSymbols(candidateTerm);
        if (normalizedCandidate.Length < MinLengthForFuzzyTrigram ||
            ContainsLanguageSymbol(candidateTerm))
        {
            return;
        }

        var candidateTrigrams = BuildPgTrigrams(candidateTerm);
        if (candidateTrigrams.Count == 0)
        {
            return;
        }

        var score = ComputeJaccard(queryTrigrams, candidateTrigrams);
        if (score > bestScore)
        {
            bestScore = score;
            bestEntry = ownerEntry;
        }
    }

    private static double ComputeJaccard(HashSet<string> left, HashSet<string> right)
    {
        var intersection = 0;
        foreach (var gram in left)
        {
            if (right.Contains(gram))
            {
                intersection++;
            }
        }

        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> BuildPgTrigrams(string input)
    {
        // Mirrors PostgreSQL pg_trgm word padding: two leading spaces + single trailing space per word.
        var words = input
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', '-', '_', '/', '.'], StringSplitOptions.RemoveEmptyEntries);

        var trigrams = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawWord in words)
        {
            var sb = new StringBuilder(rawWord.Length + 3);
            sb.Append("  ");
            for (var i = 0; i < rawWord.Length; i++)
            {
                if (char.IsLetterOrDigit(rawWord[i]))
                {
                    sb.Append(rawWord[i]);
                }
            }

            if (sb.Length <= 2)
            {
                continue;
            }

            sb.Append(' ');
            var padded = sb.ToString();
            for (var i = 0; i <= padded.Length - 3; i++)
            {
                trigrams.Add(padded.Substring(i, 3));
            }
        }

        return trigrams;
    }

    private static bool NormalizedEquals(string normalizedQuery, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return string.Equals(
            normalizedQuery,
            NormalizeCompactKeepLanguageSymbols(candidate),
            StringComparison.Ordinal);
    }

    private static string NormalizeCompactKeepLanguageSymbols(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '+' or '#')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static bool ContainsLanguageSymbol(string value)
    {
        return value.IndexOfAny(['+', '#']) >= 0;
    }

    private static string StripVersionNoiseSuffix(string value)
    {
        // Strip trailing parenthetical qualifiers like "Blender (3.6)" or "Unity (URP/HDRP)" if exact match missed.
        var parenIdx = value.IndexOf('(');
        if (parenIdx > 1 && value.EndsWith(')'))
        {
            return value[..parenIdx].Trim();
        }

        return value;
    }
}
