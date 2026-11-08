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

internal sealed class CandidateSkillsService : ICandidateSkillsService
{
    private readonly AppDbContext _dbContext;
    private readonly IValidator<SaveCandidateSkillsCommand> _validator;
    private readonly ICandidateCompletenessCalculator _completenessCalculator;
    private readonly IEvidenceService _evidenceService;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public CandidateSkillsService(
        AppDbContext dbContext,
        IValidator<SaveCandidateSkillsCommand> validator,
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

    public async Task<IReadOnlyList<CandidateSkillItem>> GetSkillsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var candidateId = await _dbContext.CandidateProfiles
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.DeletedAt == null)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidateId is null)
        {
            return [];
        }

        return await _dbContext.CandidateSkills
            .AsNoTracking()
            .Where(cs => cs.CandidateId == candidateId.Value)
            .OrderByDescending(cs => cs.IsPrimary)
            .ThenByDescending(cs => cs.Proficiency)
            .ThenBy(cs => cs.Skill!.Name)
            .Select(cs => new CandidateSkillItem(
                cs.SkillId,
                cs.Skill!.Name,
                cs.Skill.Slug,
                cs.Skill.Category,
                cs.Proficiency,
                cs.YearsUsed,
                cs.LastUsedYear,
                cs.EvidenceLevel,
                cs.IsPrimary))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<IReadOnlyList<CandidateSkillItem>>> SaveSkillsAsync(
        SaveCandidateSkillsCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<IReadOnlyList<CandidateSkillItem>>(
                Error.Validation("CandidateSkills.Invalid", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(
                c => c.UserId == command.UserId && c.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<IReadOnlyList<CandidateSkillItem>>(
                Error.Validation("CandidateProfile.Step1Required", "Complete Step 1 (Basics) before adding skills."));
        }

        var requestedIds = command.Skills.Select(s => s.SkillId).ToArray();
        var activeSkills = await _dbContext.Skills
            .AsNoTracking()
            .Where(s => requestedIds.Contains(s.Id) && s.IsActive)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        if (activeSkills.Count != requestedIds.Length)
        {
            return Result.Failure<IReadOnlyList<CandidateSkillItem>>(
                Error.Validation("CandidateSkills.UnknownOrInactiveSkill", "One or more selected skills are invalid or inactive."));
        }

        var existingRows = await _dbContext.CandidateSkills
            .Where(cs => cs.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var existingById = existingRows.ToDictionary(cs => cs.SkillId);
        var requestedSet = requestedIds.ToHashSet();

        foreach (var existing in existingRows)
        {
            if (!requestedSet.Contains(existing.SkillId))
            {
                _dbContext.CandidateSkills.Remove(existing);
            }
        }

        var updatedItems = new List<CandidateSkillItem>(command.Skills.Count);

        foreach (var input in command.Skills)
        {
            var normalizedYears = input.YearsUsed.HasValue
                ? decimal.Round(input.YearsUsed.Value, 1, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            if (existingById.TryGetValue(input.SkillId, out var existing))
            {
                existing.Proficiency = input.Proficiency;
                existing.YearsUsed = normalizedYears;
                existing.LastUsedYear = input.LastUsedYear;
                existing.IsPrimary = input.IsPrimary;
            }
            else
            {
                _dbContext.CandidateSkills.Add(new CandidateSkill
                {
                    CandidateId = profile.Id,
                    SkillId = input.SkillId,
                    Proficiency = input.Proficiency,
                    YearsUsed = normalizedYears,
                    LastUsedYear = input.LastUsedYear,
                    EvidenceLevel = SkillEvidenceLevel.Claimed,
                    IsPrimary = input.IsPrimary
                });
            }
        }

        var recomputedEvidence = await _evidenceService.RecomputeAndApplyAsync(profile.Id, cancellationToken);

        foreach (var input in command.Skills)
        {
            var normalizedYears = input.YearsUsed.HasValue
                ? decimal.Round(input.YearsUsed.Value, 1, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            var catalogSkill = activeSkills[input.SkillId];
            var resolvedLevel = recomputedEvidence.TryGetValue(input.SkillId, out var level)
                ? level
                : SkillEvidenceLevel.Claimed;

            updatedItems.Add(new CandidateSkillItem(
                SkillId: catalogSkill.Id,
                SkillName: catalogSkill.Name,
                SkillSlug: catalogSkill.Slug,
                Category: catalogSkill.Category,
                Proficiency: input.Proficiency,
                YearsUsed: normalizedYears,
                LastUsedYear: input.LastUsedYear,
                EvidenceLevel: resolvedLevel,
                IsPrimary: input.IsPrimary));
        }

        profile.OnboardingStep = Math.Max(profile.OnboardingStep, (short)4);
        profile.LastActiveAt = _clock.UtcNow;

        await _completenessCalculator.RecalculateAndApplyAsync(
            profile,
            new CandidateCompletenessCountOverrides(SkillCount: command.Skills.Count),
            cancellationToken);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.profile.step3_skills_saved",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                SkillCount = command.Skills.Count,
                PrimaryCount = command.Skills.Count(s => s.IsPrimary),
                BackedCount = updatedItems.Count(s => s.EvidenceLevel == SkillEvidenceLevel.Backed),
                VerifiedCount = updatedItems.Count(s => s.EvidenceLevel == SkillEvidenceLevel.Verified),
                profile.Completeness,
                profile.OnboardingStep
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var sorted = updatedItems
            .OrderByDescending(i => i.IsPrimary)
            .ThenByDescending(i => i.Proficiency)
            .ThenBy(i => i.SkillName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Result.Success<IReadOnlyList<CandidateSkillItem>>(sorted);
    }
}
