using System.Text.RegularExpressions;

namespace HireGameDev.Domain.Rules;

public sealed record ResumePiiRedactionResult(
    string RedactedText,
    int EmailsRedacted,
    int UrlsRedacted,
    int PhonesRedacted)
{
    public int TotalRedactions => EmailsRedacted + UrlsRedacted + PhonesRedacted;
}

/// <summary>
/// Redacts personally identifiable contact details (email addresses, URLs/handles, and phone numbers)
/// from extracted resume text BEFORE any external LLM call (Blueprint v2.0 Section 5 &amp; Section 13.F).
/// Preserves technical tokens (<c>C++</c>, <c>C#</c>, <c>Node.js</c>, <c>Three.js</c>, <c>ASP.NET</c>)
/// and employment/education date ranges (<c>2019 - 2023</c>, <c>2021-06</c>).
/// </summary>
public static class ResumePiiRedactor
{
    public const string EmailPlaceholder = "[REDACTED_EMAIL]";
    public const string UrlPlaceholder = "[REDACTED_URL]";
    public const string PhonePlaceholder = "[REDACTED_PHONE]";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex EmailRegex = new(
        @"(?:mailto:)?[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,24}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex SchemeOrWwwUrlRegex = new(
        @"\b(?:https?://|www\.)[^\s<>""'`]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex BareProfileOrPathUrlRegex = new(
        @"\b(?:(?:[A-Za-z0-9\-]+\.)*(?:linkedin\.com|github\.com|gitlab\.com|bitbucket\.org|artstation\.com|behance\.net|dribbble\.com|itch\.io|youtube\.com|youtu\.be|vimeo\.com|steampowered\.com|steamcommunity\.com|play\.google\.com|linktr\.ee|x\.com|twitter\.com)(?:/[^\s<>""'`]*)?|(?:[A-Za-z0-9\-]+\.)+[A-Za-z]{2,12}/[A-Za-z0-9._~:/?#\[\]@!$&'()*+,;=%\-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Matches international +CC phone numbers (+91 98765 43210, +1 (415) 555-0199, +44 20 7946 0958)
    // without matching C++ or "3+ years".
    private static readonly Regex InternationalPhoneRegex = new(
        @"(?<![A-Za-z0-9+])\+\d{1,3}[\s.\-]?(?:\(\d{1,5}\)[\s.\-]?)?\d(?:[\d\s.\-]{6,16}\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    // Matches Indian 10-digit mobile numbers (with optional leading 0 or 91 separator) such as 9876543210, 098765-43210, 91-98765 43210.
    private static readonly Regex IndianMobileRegex = new(
        @"(?<!\d)(?:0[\s.\-]?|91[\s.\-])?[6-9]\d{4}[\s.\-]?\d{5}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    // Matches parenthesized area-code landlines/phones such as (080) 2345-6789 or (415) 555-0199.
    private static readonly Regex AreaCodePhoneRegex = new(
        @"(?<!\d)\(\d{2,5}\)[\s.\-]?\d{3,4}[\s.\-]?\d{4}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    public static string Redact(string? rawText)
    {
        return RedactWithReport(rawText).RedactedText;
    }

    public static ResumePiiRedactionResult RedactWithReport(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new ResumePiiRedactionResult(string.Empty, 0, 0, 0);
        }

        var emailCount = 0;
        var urlCount = 0;
        var phoneCount = 0;

        // 1. Redact emails first (before domain/URL matching).
        var text = EmailRegex.Replace(rawText, _ =>
        {
            emailCount++;
            return EmailPlaceholder;
        });

        // 2. Redact explicit http(s):// and www. URLs, preserving trailing sentence punctuation.
        text = SchemeOrWwwUrlRegex.Replace(text, match => ReplaceUrlPreservingTrailingPunctuation(match.Value, ref urlCount));

        // 3. Redact bare portfolio/profile domains (linkedin.com/in/..., github.com/..., example.dev/portfolio)
        // while keeping framework names like Node.js, Three.js, Babylon.js, and ASP.NET untouched.
        text = BareProfileOrPathUrlRegex.Replace(text, match => ReplaceUrlPreservingTrailingPunctuation(match.Value, ref urlCount));

        // 4. Redact international +CC phone numbers (verified 9..15 total digits).
        text = InternationalPhoneRegex.Replace(text, match =>
        {
            var digits = CountDigits(match.Value);
            if (digits < 9 || digits > 15)
            {
                return match.Value;
            }

            phoneCount++;
            return PhonePlaceholder;
        });

        // 5. Redact Indian 10-digit mobile numbers.
        text = IndianMobileRegex.Replace(text, match =>
        {
            var digits = CountDigits(match.Value);
            if (digits < 10 || digits > 12)
            {
                return match.Value;
            }

            phoneCount++;
            return PhonePlaceholder;
        });

        // 6. Redact parenthesized area-code phone numbers.
        text = AreaCodePhoneRegex.Replace(text, match =>
        {
            var digits = CountDigits(match.Value);
            if (digits < 9 || digits > 13)
            {
                return match.Value;
            }

            phoneCount++;
            return PhonePlaceholder;
        });

        return new ResumePiiRedactionResult(text, emailCount, urlCount, phoneCount);
    }

    private static string ReplaceUrlPreservingTrailingPunctuation(string matched, ref int urlCount)
    {
        var end = matched.Length;
        while (end > 0 && matched[end - 1] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}')
        {
            end--;
        }

        if (end <= 0)
        {
            return matched;
        }

        urlCount++;
        return end == matched.Length
            ? UrlPlaceholder
            : string.Concat(UrlPlaceholder, matched.AsSpan(end));
    }

    private static int CountDigits(string value)
    {
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsAsciiDigit(value[i]))
            {
                count++;
            }
        }

        return count;
    }
}
