using FluentValidation;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

internal sealed class CandidateProfileService : ICandidateProfileService
{
    private readonly AppDbContext _dbContext;
    private readonly IValidator<SaveCandidateBasicsCommand> _basicsValidator;
    private readonly IValidator<SaveCandidateExperienceCoreCommand> _experienceCoreValidator;
    private readonly IValidator<SaveCandidatePreferencesCommand> _preferencesValidator;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public CandidateProfileService(
        AppDbContext dbContext,
        IValidator<SaveCandidateBasicsCommand> basicsValidator,
        IValidator<SaveCandidateExperienceCoreCommand> experienceCoreValidator,
        IValidator<SaveCandidatePreferencesCommand> preferencesValidator,
        IAuditLogger auditLogger,
        IClock clock)
    {
        _dbContext = dbContext;
        _basicsValidator = basicsValidator;
        _experienceCoreValidator = experienceCoreValidator;
        _preferencesValidator = preferencesValidator;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<CandidateOnboardingState> GetOnboardingStateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var userDisplayName = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var profile = await _dbContext.CandidateProfiles
            .AsNoTracking()
            .Include(c => c.City)
            .FirstOrDefaultAsync(
                c => c.UserId == userId && c.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            var emptyBreakdown = CandidateProfileRules.CalculateCompleteness(new CandidateCompletenessInput(
                HasPhoto: false,
                HasHeadline: false,
                HasBio: false,
                HasCity: false,
                SkillCount: 0,
                ExperienceCount: 0,
                EducationCount: 0,
                ShippedTitleCount: 0,
                PortfolioLinkCount: 0,
                HasExpectedCtcAndNoticePeriod: false,
                IsPhoneVerified: false,
                YearsExperience: 0m,
                Seniority: SeniorityLevel.Fresher));

            return new CandidateOnboardingState(
                UserId: userId,
                SuggestedFullName: userDisplayName,
                Profile: null,
                CompletenessBreakdown: emptyBreakdown);
        }

        var breakdown = await BuildCompletenessBreakdownAsync(profile, isPersisted: true, cancellationToken);
        var snapshot = MapSnapshot(profile);

        return new CandidateOnboardingState(
            UserId: userId,
            SuggestedFullName: userDisplayName,
            Profile: snapshot,
            CompletenessBreakdown: breakdown);
    }

    public async Task<Result<CandidateProfileSnapshot>> SaveStep1BasicsAsync(
        SaveCandidateBasicsCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _basicsValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<CandidateProfileSnapshot>(
                Error.Validation("CandidateProfile.InvalidBasics", validation.Errors[0].ErrorMessage));
        }

        var userExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == command.UserId && u.DeletedAt == null && u.IsActive, cancellationToken);
        if (!userExists)
        {
            return Result.Failure<CandidateProfileSnapshot>(
                Error.NotFound("CandidateProfile.UserNotFound", "Authenticated user account was not found."));
        }

        var city = await _dbContext.Cities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CityId && c.IsActive, cancellationToken);
        if (city is null)
        {
            return Result.Failure<CandidateProfileSnapshot>(
                Error.Validation("CandidateProfile.InvalidCity", "Select an active city from the list."));
        }

        var trimmedName = command.FullName.Trim();
        var trimmedHeadline = command.Headline.Trim();
        var normalizedBio = string.IsNullOrWhiteSpace(command.Bio) ? null : command.Bio.Trim();
        var utcNow = _clock.UtcNow;

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(c => c.UserId == command.UserId, cancellationToken);

        var isNew = profile is null;
        if (profile is null)
        {
            profile = new CandidateProfile
            {
                Id = Guid.NewGuid(),
                UserId = command.UserId,
                FullName = trimmedName,
                Headline = trimmedHeadline,
                PrimaryCategory = command.PrimaryCategory,
                CityId = city.Id,
                Bio = normalizedBio,
                Seniority = SeniorityLevel.Fresher,
                YearsExperience = 0.0m,
                Availability = CandidateAvailability.ActivelyLooking,
                OnboardingStep = 2,
                IsDiscoverable = true,
                LastActiveAt = utcNow
            };

            _dbContext.CandidateProfiles.Add(profile);
        }
        else
        {
            if (profile.DeletedAt is not null)
            {
                return Result.Failure<CandidateProfileSnapshot>(
                    Error.Forbidden("CandidateProfile.Deactivated", "This candidate profile has been deactivated."));
            }

            profile.FullName = trimmedName;
            profile.Headline = trimmedHeadline;
            profile.PrimaryCategory = command.PrimaryCategory;
            profile.CityId = city.Id;
            profile.Bio = normalizedBio;
            profile.OnboardingStep = Math.Max(profile.OnboardingStep, (short)2);
            profile.LastActiveAt = utcNow;
        }

        var breakdown = await BuildCompletenessBreakdownAsync(profile, isPersisted: !isNew, cancellationToken);
        profile.Completeness = breakdown.TotalScore;

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: isNew ? "candidate.profile.created" : "candidate.profile.step1_saved",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                profile.PrimaryCategory,
                profile.CityId,
                profile.Completeness,
                profile.OnboardingStep
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        profile.City = city;
        return Result.Success(MapSnapshot(profile));
    }

    public async Task<Result<CandidateProfileSnapshot>> SaveStep2ExperienceCoreAsync(
        SaveCandidateExperienceCoreCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _experienceCoreValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<CandidateProfileSnapshot>(
                Error.Validation("CandidateProfile.InvalidExperienceCore", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .Include(c => c.City)
            .FirstOrDefaultAsync(
                c => c.UserId == command.UserId && c.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CandidateProfileSnapshot>(
                Error.Validation("CandidateProfile.Step1Required", "Complete Step 1 (Basics) before saving Experience."));
        }

        profile.YearsExperience = decimal.Round(command.YearsExperience, 1, MidpointRounding.AwayFromZero);
        profile.Seniority = command.Seniority;
        profile.OnboardingStep = Math.Max(profile.OnboardingStep, (short)3);
        profile.LastActiveAt = _clock.UtcNow;

        var breakdown = await BuildCompletenessBreakdownAsync(profile, isPersisted: true, cancellationToken);
        profile.Completeness = breakdown.TotalScore;

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.profile.step2_saved",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                profile.YearsExperience,
                profile.Seniority,
                profile.Completeness,
                profile.OnboardingStep
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(MapSnapshot(profile));
    }

    public async Task<Result<CandidateProfileSnapshot>> SaveStep5PreferencesAsync(
        SaveCandidatePreferencesCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _preferencesValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<CandidateProfileSnapshot>(
                Error.Validation("CandidateProfile.InvalidPreferences", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .Include(c => c.City)
            .FirstOrDefaultAsync(
                c => c.UserId == command.UserId && c.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CandidateProfileSnapshot>(
                Error.Validation("CandidateProfile.Step1Required", "Complete Step 1 (Basics) before saving Preferences."));
        }

        var normalizedExpected = decimal.Round(command.ExpectedCtcLpa, 2, MidpointRounding.AwayFromZero);
        var normalizedCurrent = command.CurrentCtcLpa.HasValue
            ? decimal.Round(command.CurrentCtcLpa.Value, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        profile.ExpectedCtcLpa = normalizedExpected;
        profile.CurrentCtcLpa = normalizedCurrent;
        profile.ShowCurrentCtc = normalizedCurrent.HasValue && command.ShowCurrentCtc;
        profile.NoticePeriodDays = command.NoticePeriodDays;
        profile.OpenToRemote = command.OpenToRemote;
        profile.WillingToRelocate = command.WillingToRelocate;
        profile.Availability = command.Availability;
        profile.OnboardingStep = 5;
        profile.LastActiveAt = _clock.UtcNow;

        var breakdown = await BuildCompletenessBreakdownAsync(profile, isPersisted: true, cancellationToken);
        profile.Completeness = breakdown.TotalScore;

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.profile.step5_saved",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                profile.ExpectedCtcLpa,
                profile.ShowCurrentCtc,
                profile.NoticePeriodDays,
                profile.OpenToRemote,
                profile.WillingToRelocate,
                profile.Availability,
                profile.Completeness
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(MapSnapshot(profile));
    }

    public async Task<Result<short>> RecalculateCompletenessAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(
                c => c.UserId == userId && c.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<short>(
                Error.NotFound("CandidateProfile.NotFound", "Candidate profile was not found."));
        }

        var breakdown = await BuildCompletenessBreakdownAsync(profile, isPersisted: true, cancellationToken);
        if (profile.Completeness != breakdown.TotalScore)
        {
            profile.Completeness = breakdown.TotalScore;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(profile.Completeness);
    }

    private async Task<CandidateCompletenessBreakdown> BuildCompletenessBreakdownAsync(
        CandidateProfile profile,
        bool isPersisted,
        CancellationToken cancellationToken)
    {
        var skillCount = isPersisted
            ? await _dbContext.CandidateSkills.CountAsync(cs => cs.CandidateId == profile.Id, cancellationToken)
            : 0;

        var hasExpectedAndNotice = profile.ExpectedCtcLpa is > 0m &&
                                   profile.NoticePeriodDays.HasValue &&
                                   CandidateProfileRules.IsValidNoticePeriod(profile.NoticePeriodDays.Value);

        // Child collection counts (experiences, education, shipped titles, portfolio links)
        // plug in sequentially across M1.3 and M1.4.
        return CandidateProfileRules.CalculateCompleteness(new CandidateCompletenessInput(
            HasPhoto: !string.IsNullOrWhiteSpace(profile.PhotoPath),
            HasHeadline: !string.IsNullOrWhiteSpace(profile.Headline) && profile.Headline.Trim().Length >= 3,
            HasBio: !string.IsNullOrWhiteSpace(profile.Bio),
            HasCity: profile.CityId > 0,
            SkillCount: skillCount,
            ExperienceCount: 0,
            EducationCount: 0,
            ShippedTitleCount: 0,
            PortfolioLinkCount: 0,
            HasExpectedCtcAndNoticePeriod: hasExpectedAndNotice,
            IsPhoneVerified: profile.PhoneVerifiedAt is not null,
            YearsExperience: profile.YearsExperience,
            Seniority: profile.Seniority));
    }

    private static CandidateProfileSnapshot MapSnapshot(CandidateProfile profile)
    {
        return new CandidateProfileSnapshot(
            Id: profile.Id,
            UserId: profile.UserId,
            FullName: profile.FullName,
            Headline: profile.Headline,
            PrimaryCategory: profile.PrimaryCategory,
            CityId: profile.CityId,
            CityName: profile.City?.Name ?? string.Empty,
            CityStateCode: profile.City?.StateCode ?? string.Empty,
            PhotoPath: profile.PhotoPath,
            Phone: profile.Phone,
            PhoneVerifiedAt: profile.PhoneVerifiedAt,
            YearsExperience: profile.YearsExperience,
            Seniority: profile.Seniority,
            SuggestedSeniority: CandidateProfileRules.SuggestSeniorityFromYears(profile.YearsExperience),
            ExpectedCtcLpa: profile.ExpectedCtcLpa,
            CurrentCtcLpa: profile.CurrentCtcLpa,
            ShowCurrentCtc: profile.ShowCurrentCtc,
            NoticePeriodDays: profile.NoticePeriodDays,
            OpenToRemote: profile.OpenToRemote,
            WillingToRelocate: profile.WillingToRelocate,
            Availability: profile.Availability,
            Bio: profile.Bio,
            Completeness: profile.Completeness,
            OnboardingStep: profile.OnboardingStep,
            IsDiscoverable: profile.IsDiscoverable,
            UpdatedAt: profile.UpdatedAt);
    }
}
