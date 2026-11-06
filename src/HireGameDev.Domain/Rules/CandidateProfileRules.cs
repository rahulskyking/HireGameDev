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
    short OnboardingStep);

public sealed record CandidateCompletenessBreakdown(
    short TotalScore,
    bool MeetsApplyThreshold,
    bool MeetsDiscoverabilityThreshold,
    CandidateCompletenessItem? NextBestStep,
    IReadOnlyList<CandidateCompletenessItem> Items);

/// <summary>
/// Pure domain rules for candidate seniority derivation, notice-period validation,
/// E.164 phone checks, and 0-100 profile completeness calculation (Blueprint v2.0 Section 5).
/// </summary>
public static class CandidateProfileRules
{
    public const short ApplyCompletenessThreshold = 60;
    public const short DiscoverableCompletenessThreshold = 70;
    public const decimal MinYearsExperience = 0.0m;
    public const decimal MaxYearsExperience = 50.0m;
    public const int MinRequiredSkillsForCompleteness = 5;

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
                OnboardingStep: 1),
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
            else if (nextBest is null)
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
