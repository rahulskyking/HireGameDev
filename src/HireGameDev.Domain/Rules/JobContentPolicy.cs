using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HireGameDev.Domain.Rules;

public sealed record JobContentPolicyReport(bool HasEmail, bool HasPhone, bool HasLink, bool HasSalary, bool IsAllCaps, bool HasEmojiSpam);

/// <summary>Bounded deterministic content signals, not a fraud classifier. Does not rewrite stored content.</summary>
public static class JobContentPolicy
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);
    private static Regex Pattern(string text) => new(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Email = Pattern(@"[\p{L}\p{N}._%+\-]+\s*(?:@|\[at\]|\(at\))\s*[\p{L}\p{N}.\-]+(?:\.|\s*\[dot\]\s*)[\p{L}]{2,24}");
    private static readonly Regex Link = Pattern(@"(?:[a-z][a-z0-9+.-]{1,15}://|www\.|mailto:|tel:|\]\s*\(|<a\s+[^>]*href\s*=)");
    private static readonly Regex Domain = Pattern(@"(?<![\p{L}\p{N}_])(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:com|org|net|in|io|dev|co|ai|app|jobs|careers|gg|me|edu|xyz|info)(?![\p{L}\p{N}_])(?:/[^\s<>]*)?");
    private static readonly Regex Salary = Pattern(@"[₹$€£]|(?<![\p{L}\p{N}_])(?:INR|USD|EUR|GBP|LPA|CTC)(?![\p{L}\p{N}_])|\d\s*(?:lakhs?|lacs?|crores?|k\s*(?:/|per\s+)(?:month|year|annum))");

    public static JobContentPolicyReport Inspect(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return new(false, false, false, false, false, false);
        if (raw.Length > 8000) throw new ArgumentOutOfRangeException(nameof(raw), "Inspect only length-validated job fields.");
        var text = NormalizeForInspection(raw);
        var pii = ResumePiiRedactor.RedactWithReport(text);
        var letters = text.Where(char.IsLetter).ToArray();
        // Framework names are not application links. Explicit schemes/paths are still blocked.
        var bareDomain = Domain.Matches(text).Cast<Match>().Any(x =>
            !x.Value.Equals("ASP.NET", StringComparison.OrdinalIgnoreCase) &&
            !x.Value.Equals("Socket.IO", StringComparison.OrdinalIgnoreCase));
        var emojis = text.EnumerateRunes().Count(x => x.Value is >= 0x1F300 and <= 0x1FAFF || x.Value is >= 0x2600 and <= 0x27BF);
        return new(Email.IsMatch(text) || pii.EmailsRedacted > 0, pii.PhonesRedacted > 0,
            Link.IsMatch(text) || bareDomain || pii.UrlsRedacted > 0, Salary.IsMatch(text),
            letters.Length >= 4 && letters.Any(char.IsUpper) && !letters.Any(char.IsLower), emojis >= 3);
    }

    private static string NormalizeForInspection(string raw)
    {
        var result = new StringBuilder();
        foreach (var rune in raw.Normalize(NormalizationForm.FormKC).EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format) result.Append(rune.ToString());
        return result.ToString();
    }
}
