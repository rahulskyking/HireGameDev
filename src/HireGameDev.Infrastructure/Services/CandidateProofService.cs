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

internal sealed class CandidateProofService : ICandidateProofService
{
    private readonly AppDbContext _dbContext;
    private readonly IValidator<SaveCandidateStep4ProofCommand> _validator;
    private readonly ICandidateCompletenessCalculator _completenessCalculator;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public CandidateProofService(
        AppDbContext dbContext,
        IValidator<SaveCandidateStep4ProofCommand> validator,
        ICandidateCompletenessCalculator completenessCalculator,
        IAuditLogger auditLogger,
        IClock clock)
    {
        _dbContext = dbContext;
        _validator = validator;
        _completenessCalculator = completenessCalculator;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<CandidateOwnerProofSnapshot> GetOwnerProofForUserAsync(
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
            return new CandidateOwnerProofSnapshot([], []);
        }

        var titles = await QueryOrderedShippedTitlesAsync(profileId.Value, cancellationToken);
        var links = await QueryOrderedPortfolioLinksAsync(profileId.Value, cancellationToken);

        return new CandidateOwnerProofSnapshot(
            ShippedTitles: titles.Select(MapOwnerShippedTitle).ToArray(),
            PortfolioLinks: links);
    }

    public async Task<CandidatePublicProofSnapshot> GetPublicProofForCandidateAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.CandidateProfiles
            .AsNoTracking()
            .AnyAsync(c => c.Id == candidateId && c.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return new CandidatePublicProofSnapshot([], []);
        }

        var titles = await QueryOrderedShippedTitlesAsync(candidateId, cancellationToken);
        var links = await QueryOrderedPortfolioLinksAsync(candidateId, cancellationToken);

        return new CandidatePublicProofSnapshot(
            ShippedTitles: titles.Select(MapPublicShippedTitle).ToArray(),
            PortfolioLinks: links);
    }

    public async Task<Result<CandidateOwnerProofSnapshot>> SaveStep4ProofAsync(
        SaveCandidateStep4ProofCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<CandidateOwnerProofSnapshot>(
                Error.Validation("CandidateProof.Invalid", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(
                c => c.UserId == command.UserId && c.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CandidateOwnerProofSnapshot>(
                Error.Validation("CandidateProfile.Step1Required", "Complete Step 1 (Basics) before saving Shipped Titles & Portfolio."));
        }

        // Validate all referenced engine skills and portfolio skill IDs against active skills catalog.
        var requestedEngineIds = command.ShippedTitles
            .Where(t => t.EngineSkillId.HasValue)
            .Select(t => t.EngineSkillId!.Value)
            .Distinct()
            .ToArray();

        var requestedLinkSkillIds = command.PortfolioLinks
            .SelectMany(l => l.SkillIds)
            .Distinct()
            .ToArray();

        var allReferencedSkillIds = requestedEngineIds
            .Concat(requestedLinkSkillIds)
            .Distinct()
            .ToArray();

        var activeSkillsById = allReferencedSkillIds.Length == 0
            ? new Dictionary<int, Skill>()
            : await _dbContext.Skills
                .AsNoTracking()
                .Where(s => allReferencedSkillIds.Contains(s.Id) && s.IsActive)
                .ToDictionaryAsync(s => s.Id, cancellationToken);

        for (var i = 0; i < requestedEngineIds.Length; i++)
        {
            if (!activeSkillsById.TryGetValue(requestedEngineIds[i], out var engineSkill) ||
                engineSkill.Category != SkillCategory.Engine)
            {
                return Result.Failure<CandidateOwnerProofSnapshot>(
                    Error.Validation("CandidateProof.InvalidEngineSkill", "One or more selected game engines are not valid active engine skills."));
            }
        }

        if (requestedLinkSkillIds.Any(id => !activeSkillsById.ContainsKey(id)))
        {
            return Result.Failure<CandidateOwnerProofSnapshot>(
                Error.Validation("CandidateProof.InvalidPortfolioSkill", "One or more tagged portfolio skills are inactive or do not exist."));
        }

        // 1. Reconcile ShippedTitles in place, strictly enforcing backend-controlled Verification.
        var existingTitles = await _dbContext.ShippedTitles
            .Where(st => st.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var existingTitlesById = existingTitles.ToDictionary(st => st.Id);
        var retainedTitleIds = new HashSet<Guid>();
        var savedOwnerTitles = new List<CandidateShippedTitleOwnerItem>(command.ShippedTitles.Count);
        var verificationResetCount = 0;

        for (var idx = 0; idx < command.ShippedTitles.Count; idx++)
        {
            var input = command.ShippedTitles[idx];
            var trimmedGame = input.GameName.Trim();
            var trimmedStudio = input.StudioName.Trim();
            var trimmedRole = input.Role.Trim();
            var normalizedPlatforms = input.Platforms
                .Select(p => p.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            string? normalizedStoreUrl = null;
            if (!string.IsNullOrWhiteSpace(input.StoreUrl))
            {
                if (!ExternalUrlRules.TryNormalizeHttpsUrl(input.StoreUrl, out var canonicalStoreUrl, out var urlErr))
                {
                    return Result.Failure<CandidateOwnerProofSnapshot>(
                        Error.Validation("CandidateProof.InvalidStoreUrl", urlErr ?? "Invalid store URL."));
                }

                normalizedStoreUrl = canonicalStoreUrl;
            }

            var hideStudio = input.IsNda && input.HideStudioInPublic;
            var sortOrder = (short)idx;
            var engineName = input.EngineSkillId.HasValue && activeSkillsById.TryGetValue(input.EngineSkillId.Value, out var eng)
                ? eng.Name
                : null;

            if (input.Id.HasValue &&
                input.Id.Value != Guid.Empty &&
                existingTitlesById.TryGetValue(input.Id.Value, out var existingTitle) &&
                retainedTitleIds.Add(existingTitle.Id))
            {
                // Security invariant: if credit-defining fields change on a previously verified row,
                // reset backend verification to None so a verified badge can never be repurposed.
                var identityChanged =
                    !string.Equals(existingTitle.GameName, trimmedGame, StringComparison.Ordinal) ||
                    !string.Equals(existingTitle.StudioName, trimmedStudio, StringComparison.Ordinal) ||
                    !string.Equals(existingTitle.Role, trimmedRole, StringComparison.Ordinal) ||
                    !string.Equals(existingTitle.StoreUrl, normalizedStoreUrl, StringComparison.Ordinal);

                if (identityChanged && existingTitle.Verification != ShippedTitleVerification.None)
                {
                    existingTitle.Verification = ShippedTitleVerification.None;
                    existingTitle.VerifiedAt = null;
                    verificationResetCount++;
                }

                existingTitle.GameName = trimmedGame;
                existingTitle.StudioName = trimmedStudio;
                existingTitle.Role = trimmedRole;
                existingTitle.Platforms = normalizedPlatforms;
                existingTitle.EngineSkillId = input.EngineSkillId;
                existingTitle.ReleaseYear = input.ReleaseYear;
                existingTitle.StoreUrl = normalizedStoreUrl;
                existingTitle.IsNda = input.IsNda;
                existingTitle.HideStudioInPublic = hideStudio;
                existingTitle.SortOrder = sortOrder;

                savedOwnerTitles.Add(new CandidateShippedTitleOwnerItem(
                    Id: existingTitle.Id,
                    GameName: existingTitle.GameName,
                    StudioName: existingTitle.StudioName,
                    Role: existingTitle.Role,
                    Platforms: existingTitle.Platforms,
                    EngineSkillId: existingTitle.EngineSkillId,
                    EngineSkillName: engineName,
                    ReleaseYear: existingTitle.ReleaseYear,
                    StoreUrl: existingTitle.StoreUrl,
                    Verification: existingTitle.Verification,
                    VerifiedAt: existingTitle.VerifiedAt,
                    IsNda: existingTitle.IsNda,
                    HideStudioInPublic: existingTitle.HideStudioInPublic,
                    SortOrder: existingTitle.SortOrder));
            }
            else
            {
                var newTitle = new ShippedTitle
                {
                    Id = Guid.NewGuid(),
                    CandidateId = profile.Id,
                    GameName = trimmedGame,
                    StudioName = trimmedStudio,
                    Role = trimmedRole,
                    Platforms = normalizedPlatforms,
                    EngineSkillId = input.EngineSkillId,
                    ReleaseYear = input.ReleaseYear,
                    StoreUrl = normalizedStoreUrl,
                    Verification = ShippedTitleVerification.None,
                    VerifiedAt = null,
                    IsNda = input.IsNda,
                    HideStudioInPublic = hideStudio,
                    SortOrder = sortOrder
                };

                _dbContext.ShippedTitles.Add(newTitle);
                savedOwnerTitles.Add(new CandidateShippedTitleOwnerItem(
                    Id: newTitle.Id,
                    GameName: newTitle.GameName,
                    StudioName: newTitle.StudioName,
                    Role: newTitle.Role,
                    Platforms: newTitle.Platforms,
                    EngineSkillId: newTitle.EngineSkillId,
                    EngineSkillName: engineName,
                    ReleaseYear: newTitle.ReleaseYear,
                    StoreUrl: newTitle.StoreUrl,
                    Verification: ShippedTitleVerification.None,
                    VerifiedAt: null,
                    IsNda: newTitle.IsNda,
                    HideStudioInPublic: newTitle.HideStudioInPublic,
                    SortOrder: newTitle.SortOrder));
            }
        }

        foreach (var existingTitle in existingTitles)
        {
            if (!retainedTitleIds.Contains(existingTitle.Id))
            {
                _dbContext.ShippedTitles.Remove(existingTitle);
            }
        }

        // 2. Reconcile PortfolioLinks and PortfolioLinkSkill join rows.
        var existingLinks = await _dbContext.PortfolioLinks
            .Include(pl => pl.EvidencedSkills)
            .Where(pl => pl.CandidateId == profile.Id)
            .ToListAsync(cancellationToken);

        var existingLinksById = existingLinks.ToDictionary(pl => pl.Id);
        var retainedLinkIds = new HashSet<Guid>();
        var savedLinks = new List<CandidatePortfolioLinkItem>(command.PortfolioLinks.Count);

        for (var idx = 0; idx < command.PortfolioLinks.Count; idx++)
        {
            var input = command.PortfolioLinks[idx];
            if (!ExternalUrlRules.TryNormalizeHttpsUrl(input.Url, out var canonicalUrl, out var linkUrlErr))
            {
                return Result.Failure<CandidateOwnerProofSnapshot>(
                    Error.Validation("CandidateProof.InvalidPortfolioUrl", linkUrlErr ?? "Invalid portfolio URL."));
            }

            var trimmedTitle = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim();
            var distinctSkillIds = input.SkillIds.Distinct().ToArray();
            var sortOrder = (short)idx;

            var skillDtos = distinctSkillIds
                .Select(sid => new PortfolioEvidencedSkillItem(sid, activeSkillsById[sid].Name))
                .OrderBy(s => s.SkillName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (input.Id.HasValue &&
                input.Id.Value != Guid.Empty &&
                existingLinksById.TryGetValue(input.Id.Value, out var existingLink) &&
                retainedLinkIds.Add(existingLink.Id))
            {
                existingLink.LinkType = input.LinkType;
                existingLink.Url = canonicalUrl;
                existingLink.Title = trimmedTitle;
                existingLink.SortOrder = sortOrder;

                var desiredSet = distinctSkillIds.ToHashSet();
                var currentMappings = existingLink.EvidencedSkills.ToList();

                foreach (var mapping in currentMappings)
                {
                    if (!desiredSet.Contains(mapping.SkillId))
                    {
                        _dbContext.PortfolioLinkSkills.Remove(mapping);
                    }
                }

                var currentIdSet = currentMappings.Select(m => m.SkillId).ToHashSet();
                foreach (var sid in distinctSkillIds)
                {
                    if (!currentIdSet.Contains(sid))
                    {
                        _dbContext.PortfolioLinkSkills.Add(new PortfolioLinkSkill
                        {
                            PortfolioLinkId = existingLink.Id,
                            SkillId = sid
                        });
                    }
                }

                savedLinks.Add(new CandidatePortfolioLinkItem(
                    Id: existingLink.Id,
                    LinkType: existingLink.LinkType,
                    Url: existingLink.Url,
                    Title: existingLink.Title,
                    EvidencedSkills: skillDtos,
                    SortOrder: existingLink.SortOrder));
            }
            else
            {
                var newLinkId = Guid.NewGuid();
                var newLink = new PortfolioLink
                {
                    Id = newLinkId,
                    CandidateId = profile.Id,
                    LinkType = input.LinkType,
                    Url = canonicalUrl,
                    Title = trimmedTitle,
                    SortOrder = sortOrder
                };

                _dbContext.PortfolioLinks.Add(newLink);

                foreach (var sid in distinctSkillIds)
                {
                    _dbContext.PortfolioLinkSkills.Add(new PortfolioLinkSkill
                    {
                        PortfolioLinkId = newLinkId,
                        SkillId = sid
                    });
                }

                savedLinks.Add(new CandidatePortfolioLinkItem(
                    Id: newLinkId,
                    LinkType: newLink.LinkType,
                    Url: newLink.Url,
                    Title: newLink.Title,
                    EvidencedSkills: skillDtos,
                    SortOrder: newLink.SortOrder));
            }
        }

        foreach (var existingLink in existingLinks)
        {
            if (!retainedLinkIds.Contains(existingLink.Id))
            {
                _dbContext.PortfolioLinks.Remove(existingLink);
            }
        }

        // 3. Advance onboarding step & recalculate completeness via centralized calculator.
        profile.OnboardingStep = Math.Max(profile.OnboardingStep, (short)5);
        profile.LastActiveAt = _clock.UtcNow;

        await _completenessCalculator.RecalculateAndApplyAsync(
            profile,
            new CandidateCompletenessCountOverrides(
                ShippedTitleCount: savedOwnerTitles.Count,
                PortfolioLinkCount: savedLinks.Count),
            cancellationToken);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.profile.step4_proof_saved",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                ShippedTitleCount = savedOwnerTitles.Count,
                NdaTitleCount = savedOwnerTitles.Count(t => t.IsNda),
                PortfolioLinkCount = savedLinks.Count,
                VerificationResetCount = verificationResetCount,
                profile.Completeness,
                profile.OnboardingStep
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CandidateOwnerProofSnapshot(savedOwnerTitles, savedLinks));
    }

    private async Task<List<ShippedTitle>> QueryOrderedShippedTitlesAsync(
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ShippedTitles
            .AsNoTracking()
            .Include(st => st.EngineSkill)
            .Where(st => st.CandidateId == candidateId)
            .OrderBy(st => st.SortOrder)
            .ThenByDescending(st => st.ReleaseYear.HasValue)
            .ThenByDescending(st => st.ReleaseYear)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CandidatePortfolioLinkItem>> QueryOrderedPortfolioLinksAsync(
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        var links = await _dbContext.PortfolioLinks
            .AsNoTracking()
            .Include(pl => pl.EvidencedSkills)
                .ThenInclude(pls => pls.Skill)
            .Where(pl => pl.CandidateId == candidateId)
            .OrderBy(pl => pl.SortOrder)
            .ToListAsync(cancellationToken);

        return links
            .Select(pl => new CandidatePortfolioLinkItem(
                Id: pl.Id,
                LinkType: pl.LinkType,
                Url: pl.Url,
                Title: pl.Title,
                EvidencedSkills: pl.EvidencedSkills
                    .Where(pls => pls.Skill is not null)
                    .Select(pls => new PortfolioEvidencedSkillItem(pls.SkillId, pls.Skill!.Name))
                    .OrderBy(s => s.SkillName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SortOrder: pl.SortOrder))
            .ToArray();
    }

    private static CandidateShippedTitleOwnerItem MapOwnerShippedTitle(ShippedTitle st)
    {
        return new CandidateShippedTitleOwnerItem(
            Id: st.Id,
            GameName: st.GameName,
            StudioName: st.StudioName,
            Role: st.Role,
            Platforms: st.Platforms,
            EngineSkillId: st.EngineSkillId,
            EngineSkillName: st.EngineSkill?.Name,
            ReleaseYear: st.ReleaseYear,
            StoreUrl: st.StoreUrl,
            Verification: st.Verification,
            VerifiedAt: st.VerifiedAt,
            IsNda: st.IsNda,
            HideStudioInPublic: st.HideStudioInPublic,
            SortOrder: st.SortOrder);
    }

    private static PublicShippedTitleItem MapPublicShippedTitle(ShippedTitle st)
    {
        var redactStudio = st.IsNda && st.HideStudioInPublic;

        return new PublicShippedTitleItem(
            Id: st.Id,
            DisplayTitle: st.IsNda ? PublicShippedTitleItem.NdaProjectLabel : st.GameName,
            DisplayStudio: redactStudio ? PublicShippedTitleItem.NdaStudioLabel : st.StudioName,
            Role: st.Role,
            Platforms: st.Platforms,
            EngineSkillId: st.EngineSkillId,
            EngineSkillName: st.EngineSkill?.Name,
            ReleaseYear: st.ReleaseYear,
            PublicStoreUrl: st.IsNda ? null : st.StoreUrl,
            Verification: st.Verification,
            VerifiedAt: st.VerifiedAt,
            IsNda: st.IsNda,
            IsStudioRedacted: redactStudio,
            SortOrder: st.SortOrder);
    }
}
