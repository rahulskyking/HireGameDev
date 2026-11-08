namespace HireGameDev.Domain.Rules;

/// <summary>
/// Dedicated token-aware matcher for game-development skill names and aliases inside free-text descriptions.
/// Intentionally avoids ordinary regex <c>\b</c> word boundaries so symbol-bearing and short language names
/// (<c>C</c>, <c>C#</c>, <c>C++</c>, <c>Go</c>, <c>.NET</c>, <c>Three.js</c>, <c>Cocos2d-x</c>) match accurately
/// without false positives (<c>C#</c>/<c>C++</c>/<c>Objective-C</c>/<c>CSS</c> for <c>C</c>, or
/// <c>Godot</c>/<c>Google</c>/<c>go-to</c>/<c>CS:GO</c>/<c>go.dev</c> for <c>Go</c>).
/// </summary>
public static class SkillEvidenceTextMatcher
{
    public static bool ContainsAnySkillTerm(
        string? text,
        string skillName,
        IReadOnlyList<string>? aliases = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var sanitized = MaskHttpUrls(text);

        if (!string.IsNullOrWhiteSpace(skillName) &&
            ContainsWholeSkillTermCore(sanitized.AsSpan(), skillName))
        {
            return true;
        }

        if (aliases is null || aliases.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < aliases.Count; i++)
        {
            var alias = aliases[i];
            if (!string.IsNullOrWhiteSpace(alias) &&
                ContainsWholeSkillTermCore(sanitized.AsSpan(), alias))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsWholeSkillTerm(string? text, string? skillTerm)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(skillTerm))
        {
            return false;
        }

        var sanitized = MaskHttpUrls(text);
        return ContainsWholeSkillTermCore(sanitized.AsSpan(), skillTerm);
    }

    private static bool ContainsWholeSkillTermCore(ReadOnlySpan<char> text, string skillTerm)
    {
        var parts = skillTerm.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var firstPart = parts[0].AsSpan();
        if (firstPart.Length > text.Length)
        {
            return false;
        }

        var maxStart = text.Length - firstPart.Length;
        for (var start = 0; start <= maxStart; start++)
        {
            if (!text.Slice(start, firstPart.Length).Equals(firstPart, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!HasValidStartBoundary(text, start))
            {
                continue;
            }

            var cursor = start + firstPart.Length;
            var allPartsMatched = true;

            for (var p = 1; p < parts.Length; p++)
            {
                if (cursor >= text.Length || !char.IsWhiteSpace(text[cursor]))
                {
                    allPartsMatched = false;
                    break;
                }

                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                {
                    cursor++;
                }

                var partSpan = parts[p].AsSpan();
                if (cursor + partSpan.Length > text.Length ||
                    !text.Slice(cursor, partSpan.Length).Equals(partSpan, StringComparison.OrdinalIgnoreCase))
                {
                    allPartsMatched = false;
                    break;
                }

                cursor += partSpan.Length;
            }

            if (!allPartsMatched)
            {
                continue;
            }

            if (HasValidEndBoundary(text, cursor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasValidStartBoundary(ReadOnlySpan<char> text, int start)
    {
        if (start <= 0)
        {
            return true;
        }

        var prev = text[start - 1];

        // Never start mid-identifier (e.g., "Godot", "Battle.net" before ".NET", "AC#").
        if (IsIdentifierChar(prev))
        {
            return false;
        }

        // Never start immediately after attached language symbols '+' or '#' (e.g., "+C", "#C").
        if (IsAttachedSymbol(prev))
        {
            return false;
        }

        // Reject compound hyphenated tokens such as "Objective-C", "USB-C", "on-the-go".
        if (IsCompoundHyphen(prev) &&
            start >= 2 &&
            (IsIdentifierChar(text[start - 2]) || IsAttachedSymbol(text[start - 2])))
        {
            return false;
        }

        // Reject member/file/domain/namespace/contraction tokens:
        // "main.c", "go.dev", "CS:GO", "std::go", "c'est".
        if (prev is '.' or ':' or '\'' or '\u2019')
        {
            if (start >= 2 &&
                (IsIdentifierChar(text[start - 2]) || (prev == ':' && text[start - 2] == ':')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidEndBoundary(ReadOnlySpan<char> text, int exclusiveEnd)
    {
        if (exclusiveEnd >= text.Length)
        {
            return true;
        }

        var next = text[exclusiveEnd];

        // Never end mid-identifier (e.g., "CSS", "C4D", "Godot", "Google", "C#Sharp").
        if (IsIdentifierChar(next))
        {
            return false;
        }

        // Never end immediately before '+' or '#' (prevents "C" matching "C#" / "C++",
        // "C++" matching "C+++", or "C#" matching "C##").
        if (IsAttachedSymbol(next))
        {
            return false;
        }

        // Reject compound hyphenated tokens such as "go-to", "C-suite", "C-level".
        if (IsCompoundHyphen(next) &&
            exclusiveEnd + 1 < text.Length &&
            IsIdentifierChar(text[exclusiveEnd + 1]))
        {
            return false;
        }

        // Reject domain/extension/namespace/contraction tokens ("go.dev", "CS:GO", "c::foo", "c'est"),
        // while permitting sentence-ending punctuation ("Built in C.", "Languages: Go, C++, C#.").
        if (next is '.' or ':' or '\'' or '\u2019')
        {
            if (exclusiveEnd + 1 < text.Length &&
                (IsIdentifierChar(text[exclusiveEnd + 1]) || (next == ':' && text[exclusiveEnd + 1] == ':')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsAttachedSymbol(char c) => c is '+' or '#';

    private static bool IsCompoundHyphen(char c) => c is '-' or '\u2010' or '\u2011';

    private static string MaskHttpUrls(string text)
    {
        var httpsIdx = text.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        var httpIdx = text.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (httpsIdx < 0 && httpIdx < 0)
        {
            return text;
        }

        var buffer = text.ToCharArray();
        var i = 0;

        while (i < buffer.Length)
        {
            var span = buffer.AsSpan(i);
            var isHttps = span.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            var isHttp = !isHttps && span.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

            if (!isHttps && !isHttp)
            {
                i++;
                continue;
            }

            var urlStart = i;
            var urlEnd = i + (isHttps ? 8 : 7);

            while (urlEnd < buffer.Length &&
                   !char.IsWhiteSpace(buffer[urlEnd]) &&
                   buffer[urlEnd] is not ('"' or '\'' or '<' or '>'))
            {
                urlEnd++;
            }

            while (urlEnd > urlStart &&
                   buffer[urlEnd - 1] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}')
            {
                urlEnd--;
            }

            for (var k = urlStart; k < urlEnd; k++)
            {
                buffer[k] = ' ';
            }

            i = urlEnd;
        }

        return new string(buffer);
    }
}
