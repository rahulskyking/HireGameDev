using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

/// <summary>
/// Single centralized calculator for CandidateProfile.Completeness across all candidate services.
/// Queries child collection counts from PostgreSQL unless an explicit in-transaction override is supplied,
/// then delegates pure scoring math to CandidateProfileRules.CalculateCompleteness.
/// </summary>
internal sealed class CandidateCompletenessCalculator : ICandidateCompletenessCalculator
{
    private readonly AppDbContext _dbContext;

    public CandidateCompletenessCalculator(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CandidateCompletenessBreakdown> CalculateAsync(
        CandidateProfile profile,
        bool isPersisted = true,
        CandidateCompletenessCountOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var skillCount = overrides?.SkillCount ?? (isPersisted
            ? await _dbContext.CandidateSkills.CountAsync(cs => cs.CandidateId == profile.Id, cancellationToken)
            : 0);

        var experienceCount = overrides?.ExperienceCount ?? (isPersisted
            ? await _dbContext.CandidateExperiences.CountAsync(e => e.CandidateId == profile.Id, cancellationToken)
            : 0);

        var educationCount = overrides?.EducationCount ?? (isPersisted
            ? await _dbContext.CandidateEducation.CountAsync(e => e.CandidateId == profile.Id, cancellationToken)
            : 0);

        var shippedTitleCount = overrides?.ShippedTitleCount ?? (isPersisted
            ? await _dbContext.ShippedTitles.CountAsync(st => st.CandidateId == profile.Id, cancellationToken)
            : 0);

        var portfolioLinkCount = overrides?.PortfolioLinkCount ?? (isPersisted
            ? await _dbContext.PortfolioLinks.CountAsync(pl => pl.CandidateId == profile.Id, cancellationToken)
            : 0);

        var hasExpectedAndNotice = profile.ExpectedCtcLpa is > 0m &&
                                   profile.NoticePeriodDays.HasValue &&
                                   CandidateProfileRules.IsValidNoticePeriod(profile.NoticePeriodDays.Value);

        return CandidateProfileRules.CalculateCompleteness(new CandidateCompletenessInput(
            HasPhoto: !string.IsNullOrWhiteSpace(profile.PhotoPath),
            HasHeadline: !string.IsNullOrWhiteSpace(profile.Headline) && profile.Headline.Trim().Length >= 3,
            HasBio: !string.IsNullOrWhiteSpace(profile.Bio),
            HasCity: profile.CityId > 0,
            SkillCount: skillCount,
            ExperienceCount: experienceCount,
            EducationCount: educationCount,
            ShippedTitleCount: shippedTitleCount,
            PortfolioLinkCount: portfolioLinkCount,
            HasExpectedCtcAndNoticePeriod: hasExpectedAndNotice,
            IsPhoneVerified: profile.PhoneVerifiedAt is not null,
            YearsExperience: profile.YearsExperience,
            Seniority: profile.Seniority));
    }

    public async Task<CandidateCompletenessBreakdown> RecalculateAndApplyAsync(
        CandidateProfile profile,
        CandidateCompletenessCountOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        var breakdown = await CalculateAsync(profile, isPersisted: true, overrides: overrides, cancellationToken: cancellationToken);
        profile.Completeness = breakdown.TotalScore;
        return breakdown;
    }
}
