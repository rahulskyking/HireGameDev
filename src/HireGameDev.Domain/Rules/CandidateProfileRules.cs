using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Rules;

public sealed record CandidateCompletenessInput(
    bool HasPhoto,
    bool HasHeadline,
    bool HasBio,
    bool HasCity,
    int SkillCount,
    int ExperienceCount,
    int EducationCount,
    int ShippedTitleCount,
    int PortfolioLinkCount,
    bool HasExpectedCtcAndNoticePeriod,
    bool IsPhoneVerified,
    decimal YearsExperience,
    SeniorityLevel Seniority);

public sealed record CandidateCompletenessItem(
    string Key,
    string Label,
    short Points,
    bool IsComplete,
    short OnboardingStep,
    bool IsDeferred = false);

public sealed record CandidateCompletenessBreakdown(
    short TotalScore,
    bool MeetsApplyThreshold,
    bool MeetsDiscoverabilityThreshold,
    CandidateCompletenessItem? NextBestStep,
    IReadOnlyList<CandidateCompletenessItem> Items);

/// <summary>
/// Pure domain rules for candidate seniority derivation, notice-period validation,
/// E.164 phone checks, studio-view privacy invariants, and 0-100 profile completeness calculation (Blueprint v2.0 Section 5).
/// </summary>
public static class CandidateProfileRules
{
    public const short ApplyCompletenessThreshold = 60;
    public const short DiscoverableCompletenessThreshold = 70;
    public const decimal MinYearsExperience = 0.0m;
    public const decimal MaxYearsExperience = 50.0m;
    public const decimal MaxCandidateCtcLpa = 500.0m;
    public const int MinOnboardingSkills = 3;
    public const int MinRequiredSkillsForCompleteness = 5;
    public const int MaxCandidateSkills = 25;
    public const int MaxPrimarySkills = 5;
    public const short MinSkillLastUsedYear = 1990;
    public const short MaxSkillLastUsedYear = 2100;
    public const int MaxCandidateExperiences = 20;
    public const int MaxCandidateEducationEntries = 10;
    public const int MaxExperienceDescriptionLength = 1500;
    public const short MinEducationYear = 1950;
    public const short MaxEducationYear = 2100;
    public const int MaxShippedTitles = 20;
    public const int MaxPortfolioLinks = 15;
    public const int MaxEvidencedSkillsPerLink = 15;
    public const short MinShippedTitleReleaseYear = 1990;
    public const short MaxShippedTitleReleaseYear = 2100;
    public const int MaxBlockedStudios = 50;
    public const int MaxStudioBlockSearchTermLength = 80;
    public const int MaxStudioBlockSearchResults = 15;

    public static readonly IReadOnlyList<string> AllowedGamePlatforms = ["pc", "android", "ios", "console", "web"];
    public static readonly IReadOnlyList<short> AllowedNoticePeriodDays = [0, 15, 30, 45, 60, 90];

    public static SeniorityLevel SuggestSeniorityFromYears(decimal yearsExperience)
    {
        return yearsExperience switch
        {
            < 1.0m => SeniorityLevel.Fresher,
            < 3.0m => SeniorityLevel.Junior,
            < 7.0m => SeniorityLevel.Mid,
            < 12.0m => SeniorityLevel.Senior,
            _ => SeniorityLevel.Lead
        };
    }

    public static bool IsSeniorityWithinAllowedBand(decimal yearsExperience, SeniorityLevel selected)
    {
        if (!Enum.IsDefined(selected))
        {
            return false;
        }

        var suggested = SuggestSeniorityFromYears(yearsExperience);
        return Math.Abs((int)selected - (int)suggested) <= 1;
    }

    public static IReadOnlyList<SeniorityLevel> GetAllowedSeniorityLevels(decimal yearsExperience)
    {
        var suggested = SuggestSeniorityFromYears(yearsExperience);
        var levels = new List<SeniorityLevel>(3);

        foreach (var level in Enum.GetValues<SeniorityLevel>())
        {
            if (Math.Abs((int)level - (int)suggested) <= 1)
            {
                levels.Add(level);
            }
        }

        return levels;
    }

    public static bool IsHalfYearIncrement(decimal yearsExperience)
    {
        if (yearsExperience < MinYearsExperience || yearsExperience > MaxYearsExperience)
        {
            return false;
        }

        var doubled = yearsExperience * 2.0m;
        return doubled == decimal.Truncate(doubled);
    }

    public static bool IsValidNoticePeriod(short noticePeriodDays)
    {
        for (var i = 0; i < AllowedNoticePeriodDays.Count; i++)
        {
            if (AllowedNoticePeriodDays[i] == noticePeriodDays)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsValidGamePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return false;
        }

        var normalized = platform.Trim().ToLowerInvariant();
        for (var i = 0; i < AllowedGamePlatforms.Count; i++)
        {
            if (AllowedGamePlatforms[i] == normalized)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsValidE164Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        if (phone.Length < 9 || phone.Length > 16 || phone[0] != '+')
        {
            return false;
        }

        if (phone[1] < '1' || phone[1] > '9')
        {
            return false;
        }

        for (var i = 2; i < phone.Length; i++)
        {
            if (phone[i] < '0' || phone[i] > '9')
            {
                return false;
            }
        }

        return true;
    }

    public static decimal GetEvidenceMultiplier(SkillEvidenceLevel level)
    {
        return level switch
        {
            SkillEvidenceLevel.Verified => 1.00m,
            SkillEvidenceLevel.Backed => 0.90m,
            _ => 0.70m
        };
    }

    public static bool IsDiscoverableByStudios(bool isDiscoverable, short completeness)
    {
        return isDiscoverable && completeness >= DiscoverableCompletenessThreshold;
    }

    public static bool ShouldDisplayCurrentCtcToStudio(decimal? currentCtcLpa, bool showCurrentCtc)
    {
        return currentCtcLpa.HasValue && showCurrentCtc;
    }

    public static bool IsPhoneVisibleToStudio(bool isShortlisted = false)
    {
        return isShortlisted;
    }

    public static CandidateCompletenessBreakdown CalculateCompleteness(CandidateCompletenessInput input)
    {
        var isFresher = input.YearsExperience < 1.0m || input.Seniority == SeniorityLevel.Fresher;
        var hasExperienceCredit = input.ExperienceCount >= 1 || (isFresher && input.EducationCount >= 1);
        var experienceLabel = isFresher
            ? "Add work experience (or education as a fresher)"
            : "Add at least 1 work experience entry";

        var items = new CandidateCompletenessItem[]
        {
            new(
                Key: "proof",
                Label: "Add at least 1 shipped title or portfolio link",
                Points: 20,
                IsComplete: input.ShippedTitleCount >= 1 || input.PortfolioLinkCount >= 1,
                OnboardingStep: 4),
            new(
                Key: "skills",
                Label: "Add at least 5 skills with proficiency",
                Points: 15,
                IsComplete: input.SkillCount >= MinRequiredSkillsForCompleteness,
                OnboardingStep: 3),
            new(
                Key: "experience",
                Label: experienceLabel,
                Points: 15,
                IsComplete: hasExperienceCredit,
                OnboardingStep: 2),
            new(
                Key: "bio",
                Label: "Write a short developer bio",
                Points: 10,
                IsComplete: input.HasBio,
                OnboardingStep: 1),
            new(
                Key: "preferences",
                Label: "Set expected CTC (₹ LPA) and notice period",
                Points: 10,
                IsComplete: input.HasExpectedCtcAndNoticePeriod,
                OnboardingStep: 5),
            new(
                Key: "phone_verified",
                Label: "Verify your phone number via OTP",
                Points: 10,
                IsComplete: input.IsPhoneVerified,
                OnboardingStep: 1,
                IsDeferred: !input.IsPhoneVerified),
            new(
                Key: "photo",
                Label: "Upload a profile photo",
                Points: 5,
                IsComplete: input.HasPhoto,
                OnboardingStep: 1),
            new(
                Key: "headline",
                Label: "Add a role headline",
                Points: 5,
                IsComplete: input.HasHeadline,
                OnboardingStep: 1),
            new(
                Key: "city",
                Label: "Select your primary city",
                Points: 5,
                IsComplete: input.HasCity,
                OnboardingStep: 1),
            new(
                Key: "education",
                Label: "Add an education entry",
                Points: 5,
                IsComplete: input.EducationCount >= 1,
                OnboardingStep: 2)
        };

        short score = 0;
        CandidateCompletenessItem? nextBest = null;

        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].IsComplete)
            {
                score += items[i].Points;
            }
            else if (!items[i].IsDeferred && nextBest is null)
            {
                nextBest = items[i];
            }
        }

        if (score > 100)
        {
            score = 100;
        }

        return new CandidateCompletenessBreakdown(
            TotalScore: score,
            MeetsApplyThreshold: score >= ApplyCompletenessThreshold,
            MeetsDiscoverabilityThreshold: score >= DiscoverableCompletenessThreshold,
            NextBestStep: nextBest,
            Items: items);
    }
}
