using System.Collections.Frozen;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Rules;

public sealed record StudioEmailDomainAssessment(
    StudioEmailDomainSignal Signal,
    string? OwnerEmailDomain,
    string? WebsiteDomain,
    bool IsFreeMail);

public sealed record StudioVerificationChecklistItem(
    string Key,
    string Label,
    bool IsMet,
    string Detail);

public sealed record StudioVerificationReadiness(
    bool CanSubmit,
    bool HasVerifiablePresence,
    IReadOnlyList<StudioVerificationChecklistItem> Items);

/// <summary>
/// Pure domain rules for <c>M1.10</c> studio verification eligibility, consumer free-mail detection,
/// website-vs-email domain matching, proof URL normalization, and Admin review constraints
/// (Blueprint v2.0 Section 5, ADR-008, ADR-010, ADR-013, ADR-019).
/// </summary>
public static class StudioVerificationRules
{
    public const int MaxProofUrls = 5;
    public const int MaxApplicantNotesLength = 1000;
    public const int MinAdminReasonLength = 10;
    public const int MaxAdminReasonLength = 500;
    public const int MinRejectionFeedbackLength = 10;
    public const int MaxReviewerNotesLength = 2000;
    public const int TargetReviewSlaHours = 24;

    private static readonly FrozenSet<string> FreeEmailDomains = new[]
    {
        "gmail.com", "googlemail.com",
        "outlook.com", "hotmail.com", "live.com", "msn.com", "outlook.in", "hotmail.co.in", "live.in",
        "yahoo.com", "yahoo.co.in", "yahoo.in", "ymail.com", "rocketmail.com",
        "icloud.com", "me.com", "mac.com",
        "proton.me", "protonmail.com", "pm.me",
        "zoho.com", "zohomail.in", "zohomail.com",
        "rediffmail.com", "rediff.com",
        "aol.com", "gmx.com", "gmx.us", "mail.com", "yandex.com", "yandex.ru",
        "fastmail.com", "hey.com", "tutanota.com", "tuta.com", "tutamail.com",
        "duck.com", "inbox.com"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool TryExtractEmailDomain(string? rawEmail, out string domain)
    {
        domain = string.Empty;
        if (string.IsNullOrWhiteSpace(rawEmail))
        {
            return false;
        }

        var trimmed = rawEmail.Trim();
        var atIndex = trimmed.LastIndexOf('@');
        if (atIndex <= 0 || atIndex >= trimmed.Length - 1)
        {
            return false;
        }

        var candidate = trimmed[(atIndex + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(candidate) ||
            !candidate.Contains('.', StringComparison.Ordinal) ||
            candidate.StartsWith('.') ||
            candidate.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        domain = candidate;
        return true;
    }

    public static bool IsFreeEmailDomain(string? emailDomain)
    {
        if (string.IsNullOrWhiteSpace(emailDomain))
        {
            return false;
        }

        return FreeEmailDomains.Contains(emailDomain.Trim());
    }

    public static StudioEmailDomainAssessment AssessEmailDomain(
        string? ownerEmail,
        string? normalizedWebsiteUrl)
    {
        var hasEmailDomain = TryExtractEmailDomain(ownerEmail, out var emailDomain);
        var websiteDomain = StudioProfileRules.ExtractCompanyDomainFromWebsite(normalizedWebsiteUrl);

        if (!hasEmailDomain || IsFreeEmailDomain(emailDomain))
        {
            return new StudioEmailDomainAssessment(
                Signal: StudioEmailDomainSignal.PersonalEmail,
                OwnerEmailDomain: hasEmailDomain ? emailDomain : null,
                WebsiteDomain: websiteDomain,
                IsFreeMail: true);
        }

        if (string.IsNullOrWhiteSpace(websiteDomain))
        {
            return new StudioEmailDomainAssessment(
                Signal: StudioEmailDomainSignal.CompanyDomainNoWebsite,
                OwnerEmailDomain: emailDomain,
                WebsiteDomain: null,
                IsFreeMail: false);
        }

        var matches = AreRelatedOrganizationDomains(emailDomain, websiteDomain);
        return new StudioEmailDomainAssessment(
            Signal: matches
                ? StudioEmailDomainSignal.DomainMatched
                : StudioEmailDomainSignal.CompanyDomainUnmatched,
            OwnerEmailDomain: emailDomain,
            WebsiteDomain: websiteDomain,
            IsFreeMail: false);
    }

    public static bool AreRelatedOrganizationDomains(string domainA, string domainB)
    {
        if (string.IsNullOrWhiteSpace(domainA) || string.IsNullOrWhiteSpace(domainB))
        {
            return false;
        }

        var a = domainA.Trim().ToLowerInvariant();
        var b = domainB.Trim().ToLowerInvariant();

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        return a.EndsWith($".{b}", StringComparison.Ordinal) ||
               b.EndsWith($".{a}", StringComparison.Ordinal);
    }

    public static bool TryNormalizeProofUrls(
        IEnumerable<string>? rawUrls,
        out string[] normalizedUrls,
        out string? errorMessage)
    {
        errorMessage = null;
        if (rawUrls is null)
        {
            normalizedUrls = Array.Empty<string>();
            return true;
        }

        var list = new List<string>(MaxProofUrls);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var raw in rawUrls)
        {
            index++;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!ExternalUrlRules.TryNormalizeHttpsUrl(raw, out var canonicalUrl, out var urlErr))
            {
                normalizedUrls = Array.Empty<string>();
                errorMessage = $"Verification proof link #{index}: {urlErr}";
                return false;
            }

            if (seen.Add(canonicalUrl))
            {
                list.Add(canonicalUrl);
            }

            if (list.Count > MaxProofUrls)
            {
                normalizedUrls = Array.Empty<string>();
                errorMessage = $"You can attach up to {MaxProofUrls} verification proof links.";
                return false;
            }
        }

        normalizedUrls = list.ToArray();
        return true;
    }

    public static bool TryNormalizeApplicantNotes(
        string? rawNotes,
        out string? normalizedNotes,
        out string? errorMessage)
    {
        normalizedNotes = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawNotes))
        {
            return true;
        }

        var trimmed = rawNotes.Trim();
        if (trimmed.Length > MaxApplicantNotesLength)
        {
            errorMessage = $"Verification notes must be {MaxApplicantNotesLength} characters or fewer.";
            return false;
        }

        normalizedNotes = trimmed;
        return true;
    }

    public static StudioVerificationReadiness EvaluateReadiness(
        string? studioName,
        string? description,
        int? cityId,
        int engineSkillCount,
        string? normalizedWebsiteUrl,
        string? normalizedGstin,
        int shippedTitleStoreUrlCount,
        int proofUrlCount)
    {
        var nameTrimmed = studioName?.Trim() ?? string.Empty;
        var hasValidName = nameTrimmed.Length >= StudioProfileRules.MinStudioNameLength &&
                           nameTrimmed.Length <= StudioProfileRules.MaxStudioNameLength;

        var descLength = description?.Trim().Length ?? 0;
        var hasValidDescription = descLength >= StudioProfileRules.MinOnboardingDescriptionLength &&
                                  descLength <= StudioProfileRules.MaxDescriptionLength;

        var hasCity = cityId.HasValue && cityId.Value > 0;
        var hasEngine = engineSkillCount >= StudioProfileRules.MinRequiredEngineSkills;

        var hasWebsite = !string.IsNullOrWhiteSpace(normalizedWebsiteUrl);
        var hasGstin = !string.IsNullOrWhiteSpace(normalizedGstin);
        var hasStoreLink = shippedTitleStoreUrlCount > 0;
        var hasProofLink = proofUrlCount > 0;
        var hasVerifiablePresence = hasWebsite || hasGstin || hasStoreLink || hasProofLink;

        var presenceDetailParts = new List<string>(4);
        if (hasWebsite) presenceDetailParts.Add("Official website");
        if (hasGstin) presenceDetailParts.Add("Indian GSTIN");
        if (hasStoreLink) presenceDetailParts.Add($"{shippedTitleStoreUrlCount} store page(s)");
        if (hasProofLink) presenceDetailParts.Add($"{proofUrlCount} proof link(s)");

        var presenceDetail = hasVerifiablePresence
            ? $"Provided: {string.Join(", ", presenceDetailParts)}."
            : "Add at least one verifiable signal: official https:// website, 15-character GSTIN, a game store URL, or a proof link (LinkedIn company page, Steam/Play Store/itch.io developer page).";

        var items = new StudioVerificationChecklistItem[]
        {
            new(
                Key: "name",
                Label: "Studio name (2–100 chars)",
                IsMet: hasValidName,
                Detail: hasValidName ? nameTrimmed : "Set an official studio name."),
            new(
                Key: "description",
                Label: "Studio description (100+ chars)",
                IsMet: hasValidDescription,
                Detail: hasValidDescription
                    ? $"{descLength} characters"
                    : $"Add at least {StudioProfileRules.MinOnboardingDescriptionLength} characters describing your studio."),
            new(
                Key: "city",
                Label: "Primary Indian HQ city",
                IsMet: hasCity,
                Detail: hasCity ? "HQ city selected" : "Select your studio's primary Indian city."),
            new(
                Key: "engine",
                Label: "At least 1 game engine",
                IsMet: hasEngine,
                Detail: hasEngine ? $"{engineSkillCount} engine(s) listed" : "Select at least 1 game engine on your studio profile."),
            new(
                Key: "presence",
                Label: "Verifiable public presence or GSTIN",
                IsMet: hasVerifiablePresence,
                Detail: presenceDetail)
        };

        var canSubmit = hasValidName && hasValidDescription && hasCity && hasEngine && hasVerifiablePresence;

        return new StudioVerificationReadiness(
            CanSubmit: canSubmit,
            HasVerifiablePresence: hasVerifiablePresence,
            Items: items);
    }
}
