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

/// <summary>
/// Manages candidate discoverability, availability, compensation privacy,
/// and the verified-studio employer blocklist (M1.11 / ADR-020).
/// </summary>
public sealed class CandidateSettingsService : ICandidateSettingsService
{
    private readonly AppDbContext _dbContext;
    private readonly IValidator<UpdateCandidatePrivacySettingsCommand> _privacyValidator;
    private readonly IValidator<BlockStudioCommand> _blockValidator;
    private readonly IValidator<UnblockStudioCommand> _unblockValidator;
    private readonly ICandidateCompletenessCalculator _completenessCalculator;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public CandidateSettingsService(
        AppDbContext dbContext,
        IValidator<UpdateCandidatePrivacySettingsCommand> privacyValidator,
        IValidator<BlockStudioCommand> blockValidator,
        IValidator<UnblockStudioCommand> unblockValidator,
        ICandidateCompletenessCalculator completenessCalculator,
        IAuditLogger auditLogger,
        IClock clock)
    {
        _dbContext = dbContext;
        _privacyValidator = privacyValidator;
        _blockValidator = blockValidator;
        _unblockValidator = unblockValidator;
        _completenessCalculator = completenessCalculator;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<Result<CandidateSettingsSnapshot>> GetSettingsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<CandidateSettingsSnapshot>(
                Error.Validation("CandidateSettings.InvalidUser", "A valid user identity is required."));
        }

        var profile = await _dbContext.CandidateProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.DeletedAt == null, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CandidateSettingsSnapshot>(
                Error.NotFound(
                    "CandidateProfile.NotFound",
                    "Complete Step 1 (Basics) before managing candidate privacy settings."));
        }

        var snapshot = await BuildSnapshotAsync(profile, cancellationToken);
        return Result.Success(snapshot);
    }

    public async Task<Result<CandidateSettingsSnapshot>> UpdatePrivacySettingsAsync(
        UpdateCandidatePrivacySettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await _privacyValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<CandidateSettingsSnapshot>(
                Error.Validation("CandidateSettings.InvalidPrivacy", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(c => c.UserId == command.UserId && c.DeletedAt == null, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CandidateSettingsSnapshot>(
                Error.NotFound(
                    "CandidateProfile.NotFound",
                    "Complete Step 1 (Basics) before managing candidate privacy settings."));
        }

        var previousDiscoverable = profile.IsDiscoverable;
        var previousAvailability = profile.Availability;
        var previousShowCurrentCtc = profile.ShowCurrentCtc;
        var previousHadCurrentCtc = profile.CurrentCtcLpa.HasValue;

        var normalizedCurrentCtc = command.CurrentCtcLpa.HasValue
            ? decimal.Round(command.CurrentCtcLpa.Value, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        profile.IsDiscoverable = command.IsDiscoverable;
        profile.Availability = command.Availability;
        profile.CurrentCtcLpa = normalizedCurrentCtc;
        profile.ShowCurrentCtc = normalizedCurrentCtc.HasValue && command.ShowCurrentCtc;
        profile.LastActiveAt = _clock.UtcNow;

        var breakdown = await _completenessCalculator.RecalculateAndApplyAsync(
            profile,
            cancellationToken: cancellationToken);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.settings.privacy_updated",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            BeforeData: new
            {
                IsDiscoverable = previousDiscoverable,
                Availability = previousAvailability,
                HasCurrentCtc = previousHadCurrentCtc,
                ShowCurrentCtc = previousShowCurrentCtc
            },
            AfterData: new
            {
                profile.IsDiscoverable,
                profile.Availability,
                HasCurrentCtc = profile.CurrentCtcLpa.HasValue,
                profile.ShowCurrentCtc,
                Completeness = breakdown.TotalScore
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        var blockedStudios = await QueryBlockedStudiosAsync(profile.Id, cancellationToken);
        return Result.Success(MapSnapshot(profile, breakdown, blockedStudios));
    }

    public async Task<Result<IReadOnlyList<BlockableStudioSearchItem>>> SearchStudiosToBlockAsync(
        Guid userId,
        string? query,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<BlockableStudioSearchItem>>(
                Error.Validation("CandidateSettings.InvalidUser", "A valid user identity is required."));
        }

        var candidateId = await _dbContext.CandidateProfiles
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.DeletedAt == null)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidateId is null)
        {
            return Result.Failure<IReadOnlyList<BlockableStudioSearchItem>>(
                Error.NotFound(
                    "CandidateProfile.NotFound",
                    "Complete Step 1 (Basics) before managing studio blocklists."));
        }

        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length < 2)
        {
            return Result.Success<IReadOnlyList<BlockableStudioSearchItem>>(
                Array.Empty<BlockableStudioSearchItem>());
        }

        if (trimmed.Length > CandidateProfileRules.MaxStudioBlockSearchTermLength)
        {
            return Result.Failure<IReadOnlyList<BlockableStudioSearchItem>>(
                Error.Validation(
                    "CandidateSettings.QueryTooLong",
                    $"Studio search query must be {CandidateProfileRules.MaxStudioBlockSearchTermLength} characters or fewer."));
        }

        var lowered = trimmed.ToLowerInvariant();

        // ADR-018 / ADR-020: Search strictly active Verified studios so unverified draft studios remain confidential.
        var matches = await (
            from s in _dbContext.StudioProfiles.AsNoTracking()
            join c in _dbContext.Cities.AsNoTracking() on s.CityId equals (int?)c.Id into cityGroup
            from city in cityGroup.DefaultIfEmpty()
            where s.DeletedAt == null &&
                  s.VerificationStatus == StudioVerificationStatus.Verified &&
                  (s.Name.ToLower().Contains(lowered) || s.Slug.ToLower().Contains(lowered))
            orderby s.Name ascending
            select new
            {
                s.Id,
                s.Name,
                s.Slug,
                s.LogoPath,
                CityName = city != null ? city.Name : null,
                StateCode = city != null ? city.StateCode : null,
                s.StudioType
            })
            .Take(60)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return Result.Success<IReadOnlyList<BlockableStudioSearchItem>>(
                Array.Empty<BlockableStudioSearchItem>());
        }

        var blockedStudioIds = (await _dbContext.CandidateStudioBlocks
                .AsNoTracking()
                .Where(b => b.CandidateId == candidateId.Value)
                .Select(b => b.StudioId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var ranked = matches
            .OrderBy(s => RankStudioSearchMatch(s.Name, s.Slug, trimmed))
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(CandidateProfileRules.MaxStudioBlockSearchResults)
            .Select(s => new BlockableStudioSearchItem(
                StudioId: s.Id,
                Name: s.Name,
                Slug: s.Slug,
                LogoPath: s.LogoPath,
                CityName: s.CityName,
                StateCode: s.StateCode,
                StudioType: s.StudioType,
                IsAlreadyBlocked: blockedStudioIds.Contains(s.Id)))
            .ToArray();

        return Result.Success<IReadOnlyList<BlockableStudioSearchItem>>(ranked);
    }

    public async Task<Result<BlockedStudioItem>> BlockStudioAsync(
        BlockStudioCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await _blockValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<BlockedStudioItem>(
                Error.Validation("CandidateSettings.InvalidBlock", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(c => c.UserId == command.UserId && c.DeletedAt == null, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<BlockedStudioItem>(
                Error.NotFound(
                    "CandidateProfile.NotFound",
                    "Complete Step 1 (Basics) before managing studio blocklists."));
        }

        // ADR-018 / ADR-020: Only active Verified studios can be discovered or added to blocklists.
        var studio = await (
            from s in _dbContext.StudioProfiles.AsNoTracking()
            join c in _dbContext.Cities.AsNoTracking() on s.CityId equals (int?)c.Id into cityGroup
            from city in cityGroup.DefaultIfEmpty()
            where s.Id == command.StudioId &&
                  s.DeletedAt == null &&
                  s.VerificationStatus == StudioVerificationStatus.Verified
            select new
            {
                s.Id,
                s.Name,
                s.Slug,
                s.LogoPath,
                CityName = city != null ? city.Name : null,
                StateCode = city != null ? city.StateCode : null,
                s.StudioType
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (studio is null)
        {
            return Result.Failure<BlockedStudioItem>(
                Error.NotFound("CandidateSettings.StudioNotFound", "Verified studio was not found."));
        }

        var existingBlock = await _dbContext.CandidateStudioBlocks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.CandidateId == profile.Id && b.StudioId == studio.Id,
                cancellationToken);

        if (existingBlock is not null)
        {
            return Result.Success(new BlockedStudioItem(
                StudioId: studio.Id,
                Name: studio.Name,
                Slug: studio.Slug,
                LogoPath: studio.LogoPath,
                CityName: studio.CityName,
                StateCode: studio.StateCode,
                StudioType: studio.StudioType,
                BlockedAt: existingBlock.CreatedAt));
        }

        var activeBlockCount = await (
            from b in _dbContext.CandidateStudioBlocks.AsNoTracking()
            join s in _dbContext.StudioProfiles.AsNoTracking() on b.StudioId equals s.Id
            where b.CandidateId == profile.Id && s.DeletedAt == null
            select b.StudioId)
            .CountAsync(cancellationToken);

        if (activeBlockCount >= CandidateProfileRules.MaxBlockedStudios)
        {
            return Result.Failure<BlockedStudioItem>(
                Error.Validation(
                    "CandidateSettings.BlockLimitReached",
                    $"You can hide your profile from up to {CandidateProfileRules.MaxBlockedStudios} studios. Remove an existing studio block first."));
        }

        var now = _clock.UtcNow;
        var block = new CandidateStudioBlock
        {
            CandidateId = profile.Id,
            StudioId = studio.Id,
            CreatedAt = now
        };

        _dbContext.CandidateStudioBlocks.Add(block);
        profile.LastActiveAt = now;

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.settings.studio_blocked",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            AfterData: new
            {
                StudioId = studio.Id,
                StudioSlug = studio.Slug
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new BlockedStudioItem(
            StudioId: studio.Id,
            Name: studio.Name,
            Slug: studio.Slug,
            LogoPath: studio.LogoPath,
            CityName: studio.CityName,
            StateCode: studio.StateCode,
            StudioType: studio.StudioType,
            BlockedAt: now));
    }

    public async Task<Result> UnblockStudioAsync(
        UnblockStudioCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await _unblockValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(
                Error.Validation("CandidateSettings.InvalidUnblock", validation.Errors[0].ErrorMessage));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(c => c.UserId == command.UserId && c.DeletedAt == null, cancellationToken);

        if (profile is null)
        {
            return Result.Failure(
                Error.NotFound(
                    "CandidateProfile.NotFound",
                    "Complete Step 1 (Basics) before managing studio blocklists."));
        }

        var existingBlock = await _dbContext.CandidateStudioBlocks
            .FirstOrDefaultAsync(
                b => b.CandidateId == profile.Id && b.StudioId == command.StudioId,
                cancellationToken);

        if (existingBlock is null)
        {
            return Result.Success();
        }

        _dbContext.CandidateStudioBlocks.Remove(existingBlock);
        profile.LastActiveAt = _clock.UtcNow;

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.settings.studio_unblocked",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.UserId,
            BeforeData: new
            {
                StudioId = command.StudioId
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<bool> CanStudioViewerDiscoverCandidateAsync(
        Guid candidateId,
        Guid studioViewerUserId,
        CancellationToken cancellationToken = default)
    {
        if (candidateId == Guid.Empty || studioViewerUserId == Guid.Empty)
        {
            return false;
        }

        var candidate = await _dbContext.CandidateProfiles
            .AsNoTracking()
            .Where(c => c.Id == candidateId && c.DeletedAt == null)
            .Select(c => new
            {
                c.IsDiscoverable,
                c.Completeness
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null ||
            !CandidateProfileRules.IsDiscoverableByStudios(candidate.IsDiscoverable, candidate.Completeness))
        {
            return false;
        }

        return await HasUnblockedVerifiedStudioMembershipAsync(
            _dbContext,
            candidateId,
            studioViewerUserId,
            cancellationToken);
    }

    internal static async Task<bool> HasUnblockedVerifiedStudioMembershipAsync(
        AppDbContext dbContext,
        Guid candidateId,
        Guid studioViewerUserId,
        CancellationToken cancellationToken)
    {
        var memberStudios = await (
            from m in dbContext.StudioMembers.AsNoTracking()
            join s in dbContext.StudioProfiles.AsNoTracking() on m.StudioId equals s.Id
            where m.UserId == studioViewerUserId && s.DeletedAt == null
            select new
            {
                s.Id,
                s.VerificationStatus
            })
            .ToListAsync(cancellationToken);

        if (!memberStudios.Any(s => s.VerificationStatus == StudioVerificationStatus.Verified))
        {
            return false;
        }

        var memberStudioIds = memberStudios.Select(s => s.Id).Distinct().ToArray();
        var isBlocked = await dbContext.CandidateStudioBlocks
            .AsNoTracking()
            .AnyAsync(
                b => b.CandidateId == candidateId && memberStudioIds.Contains(b.StudioId),
                cancellationToken);

        return !isBlocked;
    }

    private async Task<CandidateSettingsSnapshot> BuildSnapshotAsync(
        CandidateProfile profile,
        CancellationToken cancellationToken)
    {
        var breakdown = await _completenessCalculator.CalculateAsync(
            profile,
            isPersisted: true,
            overrides: null,
            cancellationToken: cancellationToken);

        var blockedStudios = await QueryBlockedStudiosAsync(profile.Id, cancellationToken);
        return MapSnapshot(profile, breakdown, blockedStudios);
    }

    private async Task<IReadOnlyList<BlockedStudioItem>> QueryBlockedStudiosAsync(
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        var items = await (
            from b in _dbContext.CandidateStudioBlocks.AsNoTracking()
            join s in _dbContext.StudioProfiles.AsNoTracking() on b.StudioId equals s.Id
            join c in _dbContext.Cities.AsNoTracking() on s.CityId equals (int?)c.Id into cityGroup
            from city in cityGroup.DefaultIfEmpty()
            where b.CandidateId == candidateId && s.DeletedAt == null
            orderby b.CreatedAt descending, s.Name ascending
            select new BlockedStudioItem(
                s.Id,
                s.Name,
                s.Slug,
                s.LogoPath,
                city != null ? city.Name : null,
                city != null ? city.StateCode : null,
                s.StudioType,
                b.CreatedAt))
            .ToListAsync(cancellationToken);

        return items;
    }

    private static CandidateSettingsSnapshot MapSnapshot(
        CandidateProfile profile,
        CandidateCompletenessBreakdown breakdown,
        IReadOnlyList<BlockedStudioItem> blockedStudios)
    {
        return new CandidateSettingsSnapshot(
            CandidateId: profile.Id,
            UserId: profile.UserId,
            IsDiscoverable: profile.IsDiscoverable,
            Availability: profile.Availability,
            CurrentCtcLpa: profile.CurrentCtcLpa,
            ShowCurrentCtc: profile.ShowCurrentCtc,
            ExpectedCtcLpa: profile.ExpectedCtcLpa,
            NoticePeriodDays: profile.NoticePeriodDays,
            OpenToRemote: profile.OpenToRemote,
            WillingToRelocate: profile.WillingToRelocate,
            Phone: profile.Phone,
            PhoneVerifiedAt: profile.PhoneVerifiedAt,
            Completeness: breakdown.TotalScore,
            MeetsApplyThreshold: breakdown.MeetsApplyThreshold,
            MeetsDiscoverabilityThreshold: breakdown.MeetsDiscoverabilityThreshold,
            BlockedStudios: blockedStudios);
    }

    private static int RankStudioSearchMatch(string name, string slug, string query)
    {
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(slug, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.AsSpan().StartsWith(query.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            slug.AsSpan().StartsWith(query.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }
}
