using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

/// <summary>
/// Recomputes <c>candidate_skills.evidence_level</c> within the caller's existing <see cref="AppDbContext"/>
/// unit of work / transaction:
/// <list type="bullet">
///   <item><description><c>Verified (2)</c>: engine skill of a <c>credit_verified</c> shipped title (including NDA), or reported by any <see cref="IVerifiedSkillEvidenceProvider"/> (e.g., Phase 7 passed skill challenges).</description></item>
///   <item><description><c>Backed (1)</c>: engine skill of any shipped title (including NDA and <c>title_verified</c>), tagged in <c>portfolio_link_skills</c>, or whole-token matched in <c>candidate_experiences.description</c>.</description></item>
///   <item><description><c>Claimed (0)</c>: self-declared skill without supporting evidence.</description></item>
/// </list>
/// </summary>
internal sealed class EvidenceService : IEvidenceService
{
    private readonly AppDbContext _dbContext;
    private readonly IReadOnlyList<IVerifiedSkillEvidenceProvider> _verifiedProviders;

    public EvidenceService(
        AppDbContext dbContext,
        IEnumerable<IVerifiedSkillEvidenceProvider>? verifiedProviders = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _verifiedProviders = verifiedProviders?.ToArray() ?? [];
    }

    public async Task<IReadOnlyDictionary<int, SkillEvidenceLevel>> RecomputeAndApplyAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        if (candidateId == Guid.Empty)
        {
            return new Dictionary<int, SkillEvidenceLevel>();
        }

        // Load persisted candidate_skills into the ChangeTracker, then resolve effective live rows
        // (Added + Unchanged + Modified, excluding Deleted/Detached) so uncommitted changes in Step 3
        // participate in the same transaction.
        await _dbContext.CandidateSkills
            .Where(cs => cs.CandidateId == candidateId)
            .ToListAsync(cancellationToken);

        var liveCandidateSkills = _dbContext.CandidateSkills.Local
            .Where(cs => cs.CandidateId == candidateId && IsLiveEntity(cs))
            .ToList();

        if (liveCandidateSkills.Count == 0)
        {
            return new Dictionary<int, SkillEvidenceLevel>();
        }

        var activeSkillIds = liveCandidateSkills
            .Select(cs => cs.SkillId)
            .Distinct()
            .ToArray();

        var skillsById = await _dbContext.Skills
            .AsNoTracking()
            .Where(s => activeSkillIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var localSkill in _dbContext.Skills.Local)
        {
            if (activeSkillIds.Contains(localSkill.Id) &&
                IsLiveEntity(localSkill) &&
                !skillsById.ContainsKey(localSkill.Id))
            {
                skillsById[localSkill.Id] = localSkill;
            }
        }

        // 1. Shipped titles (NDA titles intentionally contribute to internal skill evidence regardless
        //    of IsNda or HideStudioInPublic).
        await _dbContext.ShippedTitles
            .Where(st => st.CandidateId == candidateId)
            .ToListAsync(cancellationToken);

        var liveShippedTitles = _dbContext.ShippedTitles.Local
            .Where(st => st.CandidateId == candidateId && IsLiveEntity(st))
            .ToList();

        var creditVerifiedEngineSkillIds = new HashSet<int>();
        var allShippedEngineSkillIds = new HashSet<int>();

        for (var i = 0; i < liveShippedTitles.Count; i++)
        {
            var title = liveShippedTitles[i];
            if (!title.EngineSkillId.HasValue)
            {
                continue;
            }

            var engineSkillId = title.EngineSkillId.Value;
            allShippedEngineSkillIds.Add(engineSkillId);

            if (title.Verification == ShippedTitleVerification.CreditVerified)
            {
                creditVerifiedEngineSkillIds.Add(engineSkillId);
            }
        }

        // 2. Portfolio links & relational portfolio_link_skills join rows (only on non-deleted links).
        await _dbContext.PortfolioLinks
            .Include(pl => pl.EvidencedSkills)
            .Where(pl => pl.CandidateId == candidateId)
            .ToListAsync(cancellationToken);

        var livePortfolioLinkIds = _dbContext.PortfolioLinks.Local
            .Where(pl => pl.CandidateId == candidateId && IsLiveEntity(pl))
            .Select(pl => pl.Id)
            .ToHashSet();

        if (livePortfolioLinkIds.Count > 0)
        {
            await _dbContext.PortfolioLinkSkills
                .Where(pls => livePortfolioLinkIds.Contains(pls.PortfolioLinkId))
                .ToListAsync(cancellationToken);
        }

        var portfolioBackedSkillIds = livePortfolioLinkIds.Count == 0
            ? new HashSet<int>()
            : _dbContext.PortfolioLinkSkills.Local
                .Where(pls => livePortfolioLinkIds.Contains(pls.PortfolioLinkId) && IsLiveEntity(pls))
                .Select(pls => pls.SkillId)
                .ToHashSet();

        // 3. Work experience descriptions (token-aware matching for skill names and aliases).
        await _dbContext.CandidateExperiences
            .Where(e => e.CandidateId == candidateId)
            .ToListAsync(cancellationToken);

        var liveExperienceDescriptions = _dbContext.CandidateExperiences.Local
            .Where(e => e.CandidateId == candidateId && IsLiveEntity(e) && !string.IsNullOrWhiteSpace(e.Description))
            .Select(e => e.Description!)
            .ToArray();

        // 4. Independent external/future verified evidence providers (e.g., Phase 7 passed skill challenges).
        var providerVerifiedSkillIds = new HashSet<int>();
        for (var i = 0; i < _verifiedProviders.Count; i++)
        {
            var verifiedIds = await _verifiedProviders[i].GetVerifiedSkillIdsAsync(candidateId, cancellationToken);
            if (verifiedIds is { Count: > 0 })
            {
                providerVerifiedSkillIds.UnionWith(verifiedIds);
            }
        }

        // 5. Recompute and apply idempotently on tracked CandidateSkill rows.
        var results = new Dictionary<int, SkillEvidenceLevel>(liveCandidateSkills.Count);

        for (var i = 0; i < liveCandidateSkills.Count; i++)
        {
            var candidateSkill = liveCandidateSkills[i];
            var skillId = candidateSkill.SkillId;

            SkillEvidenceLevel computedLevel;
            if (creditVerifiedEngineSkillIds.Contains(skillId) ||
                providerVerifiedSkillIds.Contains(skillId))
            {
                computedLevel = SkillEvidenceLevel.Verified;
            }
            else if (allShippedEngineSkillIds.Contains(skillId) ||
                     portfolioBackedSkillIds.Contains(skillId) ||
                     MatchesAnyDescription(skillId, skillsById, liveExperienceDescriptions))
            {
                computedLevel = SkillEvidenceLevel.Backed;
            }
            else
            {
                computedLevel = SkillEvidenceLevel.Claimed;
            }

            if (candidateSkill.EvidenceLevel != computedLevel)
            {
                candidateSkill.EvidenceLevel = computedLevel;
            }

            results[skillId] = computedLevel;
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<int, SkillEvidenceLevel>> RecomputeAndSaveAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var results = await RecomputeAndApplyAsync(candidateId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return results;
    }

    private bool IsLiveEntity<TEntity>(TEntity entity)
        where TEntity : class
    {
        var state = _dbContext.Entry(entity).State;
        return state is not (EntityState.Deleted or EntityState.Detached);
    }

    private static bool MatchesAnyDescription(
        int skillId,
        IReadOnlyDictionary<int, Skill> skillsById,
        string[] descriptions)
    {
        if (descriptions.Length == 0 || !skillsById.TryGetValue(skillId, out var skill))
        {
            return false;
        }

        for (var i = 0; i < descriptions.Length; i++)
        {
            if (SkillEvidenceTextMatcher.ContainsAnySkillTerm(descriptions[i], skill.Name, skill.Aliases))
            {
                return true;
            }
        }

        return false;
    }
}
