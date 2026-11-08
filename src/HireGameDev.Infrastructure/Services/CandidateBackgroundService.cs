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

internal sealed class CandidateBackgroundService : ICandidateBackgroundService
{
    private readonly AppDbContext _dbContext;
    private readonly IValidator<SaveCandidateStep2BackgroundCommand> _validator;
    private readonly ICandidateCompletenessCalculator _completenessCalculator;
    private readonly IEvidenceService _evidenceService;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public CandidateBackgroundService(
        AppDbContext dbContext,
        IValidator<SaveCandidateStep2BackgroundCommand> validator,
        ICandidateCompletenessCalculator completenessCalculator,
        IEvidenceService evidenceService,
        IAuditLogger auditLogger,
        IClock clock)
    {
        _dbContext = dbContext;
        _validator = validator;
        _completenessCalculator = completenessCalculator;
        _evidenceService = evidenceService;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<CandidateBackgroundSnapshot> GetBackgroundForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profileId = await _dbContext.CandidateProfiles
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.DeletedAt == null)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (profileId is null)
        {
            return new CandidateBackgroundSnapshot([], []);
        }

        var experiences = await _dbContext.CandidateExperiences
            .AsNoTracking()
            .Where(e => e.CandidateId == profileId.Value)
            .OrderByDescending(e => e.IsCurrent)
            .ThenByDescending(e => e.StartDate)
            .ThenBy(e => e.CompanyName)
            .Select(e => new CandidateExperienceItem(
                e.Id,
                e.CompanyName,
                e.Title,
                e.StartDate,
                e.EndDate,
                e.IsCurrent,
                e.Description))
            .ToListAsync(cancellationToken);

        var education = await _dbContext.CandidateEducation
            .AsNoTracking()
            .Where(e => e.CandidateId == profileId.Value)
            .OrderByDescending(e => e.EndYear.HasValue)
            .ThenByDescending(e => e.EndYear)
            .ThenByDescending(e => e.StartYear)
            .ThenBy(e => e.Institution)
            .Select(e => new CandidateEducationItem(
                e.Id,
                e.Institution,
                e.Degree,
                e.Field,
                e.StartYear,
                e.EndYear))
            .ToListAsync(cancellationToken);

        return new CandidateBackgroundSnapshot(experiences, education);
    }

    public async Task<Result<CandidateBackgroundSnapshot>> SaveStep2BackgroundAsync(
        SaveCandidateStep2BackgroundCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<CandidateBackgroundSnapshot>(
                Error.Validation("CandidateBackground.Invalid", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(
                c => c.UserId == command.UserId && c.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CandidateBackgroundSnapshot>(
                Error.Validation("CandidateProfile.Step1Required", "Complete Step 1 (Basics) before saving Experience & Education."));
        }

        var existingExperiences = await _dbContext.CandidateExperiences
            .Where(e => e.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var existingExpById = existingExperiences.ToDictionary(e => e.Id);
        var retainedExpIds = new HashSet<Guid>();
        var savedExpItems = new List<CandidateExperienceItem>(command.Experiences.Count);

        foreach (var input in command.Experiences)
        {
            var trimmedCompany = input.CompanyName.Trim();
            var trimmedTitle = input.Title.Trim();
            var normalizedEnd = input.IsCurrent ? null : input.EndDate;
            var trimmedDesc = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();

            if (input.Id.HasValue &&
                input.Id.Value != Guid.Empty &&
                existingExpById.TryGetValue(input.Id.Value, out var existingExp) &&
                retainedExpIds.Add(existingExp.Id))
            {
                existingExp.CompanyName = trimmedCompany;
                existingExp.Title = trimmedTitle;
                existingExp.StartDate = input.StartDate;
                existingExp.EndDate = normalizedEnd;
                existingExp.IsCurrent = input.IsCurrent;
                existingExp.Description = trimmedDesc;

                savedExpItems.Add(new CandidateExperienceItem(
                    existingExp.Id,
                    existingExp.CompanyName,
                    existingExp.Title,
                    existingExp.StartDate,
                    existingExp.EndDate,
                    existingExp.IsCurrent,
                    existingExp.Description));
            }
            else
            {
                var newExp = new CandidateExperience
                {
                    Id = Guid.NewGuid(),
                    CandidateId = profile.Id,
                    CompanyName = trimmedCompany,
                    Title = trimmedTitle,
                    StartDate = input.StartDate,
                    EndDate = normalizedEnd,
                    IsCurrent = input.IsCurrent,
                    Description = trimmedDesc
                };

                _dbContext.CandidateExperiences.Add(newExp);
                savedExpItems.Add(new CandidateExperienceItem(
                    newExp.Id,
                    newExp.CompanyName,
                    newExp.Title,
                    newExp.StartDate,
                    newExp.EndDate,
                    newExp.IsCurrent,
                    newExp.Description));
            }
        }

        foreach (var existingExp in existingExperiences)
        {
            if (!retainedExpIds.Contains(existingExp.Id))
            {
                _dbContext.CandidateExperiences.Remove(existingExp);
            }
        }

        var existingEducation = await _dbContext.CandidateEducation
            .Where(e => e.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var existingEduById = existingEducation.ToDictionary(e => e.Id);
        var retainedEduIds = new HashSet<Guid>();
        var savedEduItems = new List<CandidateEducationItem>(command.Education.Count);

        foreach (var input in command.Education)
        {
            var trimmedInstitution = input.Institution.Trim();
            var trimmedDegree = input.Degree.Trim();
            var trimmedField = string.IsNullOrWhiteSpace(input.Field) ? null : input.Field.Trim();

            if (input.Id.HasValue &&
                input.Id.Value != Guid.Empty &&
                existingEduById.TryGetValue(input.Id.Value, out var existingEdu) &&
                retainedEduIds.Add(existingEdu.Id))
            {
                existingEdu.Institution = trimmedInstitution;
                existingEdu.Degree = trimmedDegree;
                existingEdu.Field = trimmedField;
                existingEdu.StartYear = input.StartYear;
                existingEdu.EndYear = input.EndYear;

                savedEduItems.Add(new CandidateEducationItem(
                    existingEdu.Id,
                    existingEdu.Institution,
                    existingEdu.Degree,
                    existingEdu.Field,
                    existingEdu.StartYear,
                    existingEdu.EndYear));
            }
            else
            {
                var newEdu = new CandidateEducation
                {
                    Id = Guid.NewGuid(),
                    CandidateId = profile.Id,
                    Institution = trimmedInstitution,
                    Degree = trimmedDegree,
                    Field = trimmedField,
                    StartYear = input.StartYear,
                    EndYear = input.EndYear
                };

                _dbContext.CandidateEducation.Add(newEdu);
                savedEduItems.Add(new CandidateEducationItem(
                    newEdu.Id,
                    newEdu.Institution,
                    newEdu.Degree,
                    newEdu.Field,
                    newEdu.StartYear,
                    newEdu.EndYear));
            }
        }

        foreach (var existingEdu in existingEducation)
        {
            if (!retainedEduIds.Contains(existingEdu.Id))
            {
                _dbContext.CandidateEducation.Remove(existingEdu);
            }
        }

        profile.YearsExperience = decimal.Round(command.YearsExperience, 1, MidpointRounding.AwayFromZero);
        profile.Seniority = command.Seniority;
        profile.OnboardingStep = Math.Max(profile.OnboardingStep, (short)3);
        profile.LastActiveAt = _clock.UtcNow;

        await _completenessCalculator.RecalculateAndApplyAsync(
            profile,
            new CandidateCompletenessCountOverrides(
                ExperienceCount: savedExpItems.Count,
                EducationCount: savedEduItems.Count),
            cancellationToken);

        await _evidenceService.RecomputeAndApplyAsync(profile.Id, cancellationToken);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.profile.step2_background_saved",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                profile.YearsExperience,
                profile.Seniority,
                ExperienceCount = savedExpItems.Count,
                EducationCount = savedEduItems.Count,
                profile.Completeness,
                profile.OnboardingStep
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var orderedExp = savedExpItems
            .OrderByDescending(e => e.IsCurrent)
            .ThenByDescending(e => e.StartDate)
            .ThenBy(e => e.CompanyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var orderedEdu = savedEduItems
            .OrderByDescending(e => e.EndYear.HasValue)
            .ThenByDescending(e => e.EndYear)
            .ThenByDescending(e => e.StartYear)
            .ThenBy(e => e.Institution, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Result.Success(new CandidateBackgroundSnapshot(orderedExp, orderedEdu));
    }
}
