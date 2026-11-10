using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Constants;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

public sealed class StudioProfileService : IStudioProfileService
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public StudioProfileService(
        AppDbContext dbContext,
        IFileStorageService fileStorage,
        IAuditLogger auditLogger,
        IClock clock)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StudioMembershipSummary>> GetUserMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<StudioMembershipSummary>();
        }

        var rows = await _dbContext.StudioMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Studio.DeletedAt == null)
            .Select(m => new
            {
                m.StudioId,
                m.Studio.Name,
                m.Studio.Slug,
                m.Studio.LogoPath,
                m.MemberRole,
                m.Studio.VerificationStatus,
                CityName = m.Studio.City != null ? m.Studio.City.Name : null,
                m.InvitedAt
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.MemberRole == StudioMemberRole.Owner ? 0 : r.MemberRole == StudioMemberRole.Recruiter ? 1 : 2)
            .ThenBy(r => r.InvitedAt)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new StudioMembershipSummary(
                StudioId: r.StudioId,
                Name: r.Name,
                Slug: r.Slug,
                LogoUrl: ResolveLogoUrlOrNull(r.LogoPath),
                MemberRole: r.MemberRole,
                VerificationStatus: r.VerificationStatus,
                CityName: r.CityName))
            .ToArray();
    }

    public async Task<StudioMembershipSummary?> GetOwnedActiveStudioSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        var owned = await _dbContext.StudioProfiles
            .AsNoTracking()
            .Where(s => s.OwnerUserId == userId && s.DeletedAt == null)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Slug,
                s.LogoPath,
                s.VerificationStatus,
                CityName = s.City != null ? s.City.Name : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (owned is null)
        {
            return null;
        }

        return new StudioMembershipSummary(
            StudioId: owned.Id,
            Name: owned.Name,
            Slug: owned.Slug,
            LogoUrl: ResolveLogoUrlOrNull(owned.LogoPath),
            MemberRole: StudioMemberRole.Owner,
            VerificationStatus: owned.VerificationStatus,
            CityName: owned.CityName);
    }

    public async Task<Result<StudioProfileDetail>> GetStudioForMemberAsync(
        Guid actorUserId,
        Guid? requestedStudioId = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Failure<StudioProfileDetail>(
                Error.Unauthorized("StudioProfile.AuthRequired", "Sign in to access your studio workspace."));
        }

        Guid targetStudioId;
        StudioMemberRole callerRole;

        if (requestedStudioId.HasValue && requestedStudioId.Value != Guid.Empty)
        {
            var studioExists = await _dbContext.StudioProfiles
                .AsNoTracking()
                .AnyAsync(s => s.Id == requestedStudioId.Value && s.DeletedAt == null, cancellationToken);

            if (!studioExists)
            {
                return Result.Failure<StudioProfileDetail>(
                    Error.NotFound("StudioProfile.NotFound", "Studio profile was not found."));
            }

            var membership = await _dbContext.StudioMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.StudioId == requestedStudioId.Value && m.UserId == actorUserId,
                    cancellationToken);

            if (membership is null)
            {
                return Result.Failure<StudioProfileDetail>(
                    Error.Forbidden("StudioProfile.Forbidden", "You are not a member of this studio workspace."));
            }

            targetStudioId = membership.StudioId;
            callerRole = membership.MemberRole;
        }
        else
        {
            var memberships = await _dbContext.StudioMembers
                .AsNoTracking()
                .Where(m => m.UserId == actorUserId && m.Studio.DeletedAt == null)
                .Select(m => new { m.StudioId, m.MemberRole, m.InvitedAt })
                .ToListAsync(cancellationToken);

            var primary = memberships
                .OrderBy(m => m.MemberRole == StudioMemberRole.Owner ? 0 : m.MemberRole == StudioMemberRole.Recruiter ? 1 : 2)
                .ThenBy(m => m.InvitedAt)
                .FirstOrDefault();

            if (primary is null)
            {
                return Result.Failure<StudioProfileDetail>(
                    Error.NotFound("StudioProfile.NotOnboarded", "Complete studio onboarding to set up your studio workspace."));
            }

            targetStudioId = primary.StudioId;
            callerRole = primary.MemberRole;
        }

        return await BuildStudioProfileDetailAsync(targetStudioId, callerRole, cancellationToken);
    }

    public async Task<Result<StudioSlugAvailabilityResult>> CheckSlugAvailabilityAsync(
        string rawSlug,
        Guid? excludeStudioId = null,
        CancellationToken cancellationToken = default)
    {
        if (!StudioProfileRules.TryNormalizeSlug(rawSlug, out var normalizedSlug, out var slugError))
        {
            var attempted = string.IsNullOrWhiteSpace(rawSlug)
                ? string.Empty
                : rawSlug.Trim().ToLowerInvariant();

            return Result.Success(new StudioSlugAvailabilityResult(
                NormalizedSlug: attempted,
                IsAvailable: false,
                Reason: slugError));
        }

        var exists = await _dbContext.StudioProfiles
            .AsNoTracking()
            .AnyAsync(
                s => s.Slug == normalizedSlug && (!excludeStudioId.HasValue || s.Id != excludeStudioId.Value),
                cancellationToken);

        return Result.Success(new StudioSlugAvailabilityResult(
            NormalizedSlug: normalizedSlug,
            IsAvailable: !exists,
            Reason: exists ? $"The URL slug '{normalizedSlug}' is already taken." : null));
    }

    public async Task<Result<StudioProfileDetail>> CreateStudioAsync(
        CreateStudioProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;
        var slugWasExplicit = !string.IsNullOrWhiteSpace(command.Slug);

        var validation = StudioValidators.ValidateProfilePayload(
            actorUserId: command.ActorUserId,
            rawName: command.Name,
            rawSlug: command.Slug,
            rawWebsite: command.Website,
            rawDescription: command.Description,
            cityId: command.CityId,
            sizeBand: command.SizeBand,
            studioType: command.StudioType,
            foundedYear: command.FoundedYear,
            rawCultureTags: command.CultureTags,
            rawGstin: command.Gstin,
            rawSkillIds: command.SkillIds,
            rawTitles: command.Titles,
            currentUtcYear: now.Year);

        if (validation.IsFailure)
        {
            return Result.Failure<StudioProfileDetail>(validation.Error);
        }

        var data = validation.Value;

        var actorUser = await _dbContext.Users
            .FirstOrDefaultAsync(
                u => u.Id == command.ActorUserId && u.IsActive && u.DeletedAt == null,
                cancellationToken);

        if (actorUser is null)
        {
            return Result.Failure<StudioProfileDetail>(
                Error.Unauthorized("StudioProfile.UserNotFound", "Active user account was not found."));
        }

        // ADR-018 Decision #3: Each user can own at most 1 active studio.
        var alreadyOwnsStudio = await _dbContext.StudioProfiles
            .AsNoTracking()
            .AnyAsync(s => s.OwnerUserId == command.ActorUserId && s.DeletedAt == null, cancellationToken);

        if (alreadyOwnsStudio)
        {
            return Result.Failure<StudioProfileDetail>(
                Error.Conflict(
                    "StudioProfile.AlreadyOwned",
                    "You already own an active studio profile. Edit your existing studio workspace instead."));
        }

        var resolvedSlugResult = await ResolveCreationSlugAsync(data.Slug, slugWasExplicit, cancellationToken);
        if (resolvedSlugResult.IsFailure)
        {
            return Result.Failure<StudioProfileDetail>(resolvedSlugResult.Error);
        }

        var finalSlug = resolvedSlugResult.Value;

        var referenceCheck = await ValidateCityAndSkillsAsync(data.CityId, data.DistinctSkillIds, cancellationToken);
        if (referenceCheck.IsFailure)
        {
            return Result.Failure<StudioProfileDetail>(referenceCheck.Error);
        }

        var studioId = Guid.NewGuid();
        var studio = new StudioProfile
        {
            Id = studioId,
            OwnerUserId = command.ActorUserId,
            Name = data.Name,
            Slug = finalSlug,
            Website = data.Website,
            CompanyEmailDomain = data.CompanyEmailDomain,
            Description = data.Description,
            CityId = data.CityId,
            SizeBand = data.SizeBand,
            StudioType = data.StudioType,
            FoundedYear = data.FoundedYear,
            CultureTags = data.CultureTags,
            Gstin = data.Gstin,
            VerificationStatus = StudioVerificationStatus.Unverified
        };

        _dbContext.StudioProfiles.Add(studio);

        _dbContext.StudioMembers.Add(new StudioMember
        {
            StudioId = studioId,
            UserId = command.ActorUserId,
            MemberRole = StudioMemberRole.Owner,
            InvitedAt = now,
            AcceptedAt = now
        });

        for (var i = 0; i < data.DistinctSkillIds.Length; i++)
        {
            _dbContext.StudioSkills.Add(new StudioSkill
            {
                StudioId = studioId,
                SkillId = data.DistinctSkillIds[i]
            });
        }

        for (var i = 0; i < data.Titles.Count; i++)
        {
            var title = data.Titles[i];
            _dbContext.StudioTitles.Add(new StudioTitle
            {
                Id = Guid.NewGuid(),
                StudioId = studioId,
                GameName = title.GameName,
                ReleaseYear = title.ReleaseYear,
                Platforms = title.Platforms,
                StoreUrl = title.StoreUrl,
                SortOrder = (short)i
            });
        }

        await EnsureUserHasStudioRoleAsync(command.ActorUserId, cancellationToken);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "studio.profile.created",
            EntityType: "studio_profile",
            EntityId: studioId.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.ActorUserId,
            AfterData: new
            {
                studio.Name,
                studio.Slug,
                studio.CityId,
                SizeBand = studio.SizeBand.ToDbValue(),
                studio.StudioType,
                SkillCount = data.DistinctSkillIds.Length,
                TitleCount = data.Titles.Count,
                HasGstin = !string.IsNullOrEmpty(studio.Gstin)
            }));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Failure<StudioProfileDetail>(
                Error.Conflict(
                    "StudioProfile.CreateConflict",
                    "Could not create studio profile because the URL slug or studio ownership was claimed concurrently. Please retry."));
        }

        return await BuildStudioProfileDetailAsync(studioId, StudioMemberRole.Owner, cancellationToken);
    }

    public async Task<Result<StudioProfileDetail>> UpdateStudioProfileAsync(
        UpdateStudioProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.StudioId == Guid.Empty)
        {
            return Result.Failure<StudioProfileDetail>(
                Error.Validation("StudioProfile.InvalidStudio", "A valid studio identifier is required."));
        }

        var studio = await _dbContext.StudioProfiles
            .FirstOrDefaultAsync(s => s.Id == command.StudioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioProfileDetail>(
                Error.NotFound("StudioProfile.NotFound", "Studio profile was not found."));
        }

        var callerMember = await _dbContext.StudioMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == command.ActorUserId,
                cancellationToken);

        if (callerMember is null || !StudioProfileRules.CanEditProfile(callerMember.MemberRole))
        {
            return Result.Failure<StudioProfileDetail>(
                Error.Forbidden(
                    "StudioProfile.EditForbidden",
                    "Only the studio Owner or a Recruiter can edit the studio profile."));
        }

        // Determine effective slug and enforce ADR-018 slug lock / owner-only slug change rules.
        var rawEffectiveSlug = string.IsNullOrWhiteSpace(command.Slug)
            ? studio.Slug
            : command.Slug;

        var now = _clock.UtcNow;
        var validation = StudioValidators.ValidateProfilePayload(
            actorUserId: command.ActorUserId,
            rawName: command.Name,
            rawSlug: rawEffectiveSlug,
            rawWebsite: command.Website,
            rawDescription: command.Description,
            cityId: command.CityId,
            sizeBand: command.SizeBand,
            studioType: command.StudioType,
            foundedYear: command.FoundedYear,
            rawCultureTags: command.CultureTags,
            rawGstin: command.Gstin,
            rawSkillIds: command.SkillIds,
            rawTitles: command.Titles,
            currentUtcYear: now.Year);

        if (validation.IsFailure)
        {
            return Result.Failure<StudioProfileDetail>(validation.Error);
        }

        var data = validation.Value;
        var slugChanged = !string.Equals(studio.Slug, data.Slug, StringComparison.OrdinalIgnoreCase);

        if (slugChanged)
        {
            if (callerMember.MemberRole != StudioMemberRole.Owner)
            {
                return Result.Failure<StudioProfileDetail>(
                    Error.Forbidden(
                        "StudioProfile.SlugOwnerOnly",
                        "Only the studio Owner can change the public studio URL slug."));
            }

            if (!StudioProfileRules.CanEditSlug(callerMember.MemberRole, studio.VerificationStatus))
            {
                return Result.Failure<StudioProfileDetail>(
                    Error.Validation(
                        "StudioProfile.SlugLocked",
                        "Your studio URL slug is locked once verification is pending or completed so public studio links remain stable."));
            }

            var slugTaken = await _dbContext.StudioProfiles
                .AsNoTracking()
                .AnyAsync(s => s.Slug == data.Slug && s.Id != studio.Id, cancellationToken);

            if (slugTaken)
            {
                return Result.Failure<StudioProfileDetail>(
                    Error.Conflict("StudioProfile.SlugTaken", $"The URL slug '{data.Slug}' is already in use."));
            }
        }

        var referenceCheck = await ValidateCityAndSkillsAsync(data.CityId, data.DistinctSkillIds, cancellationToken);
        if (referenceCheck.IsFailure)
        {
            return Result.Failure<StudioProfileDetail>(referenceCheck.Error);
        }

        var previousSlug = studio.Slug;

        studio.Name = data.Name;
        studio.Slug = data.Slug;
        studio.Website = data.Website;
        studio.CompanyEmailDomain = data.CompanyEmailDomain;
        studio.Description = data.Description;
        studio.CityId = data.CityId;
        studio.SizeBand = data.SizeBand;
        studio.StudioType = data.StudioType;
        studio.FoundedYear = data.FoundedYear;
        studio.CultureTags = data.CultureTags;
        studio.Gstin = data.Gstin;

        // Reconcile studio_skills.
        var existingSkills = await _dbContext.StudioSkills
            .Where(ss => ss.StudioId == studio.Id)
            .ToListAsync(cancellationToken);

        var targetSkillSet = data.DistinctSkillIds.ToHashSet();
        var existingSkillSet = existingSkills.Select(ss => ss.SkillId).ToHashSet();

        foreach (var existingSkill in existingSkills)
        {
            if (!targetSkillSet.Contains(existingSkill.SkillId))
            {
                _dbContext.StudioSkills.Remove(existingSkill);
            }
        }

        for (var i = 0; i < data.DistinctSkillIds.Length; i++)
        {
            var skillId = data.DistinctSkillIds[i];
            if (!existingSkillSet.Contains(skillId))
            {
                _dbContext.StudioSkills.Add(new StudioSkill
                {
                    StudioId = studio.Id,
                    SkillId = skillId
                });
            }
        }

        // Reconcile studio_titles while preserving IgdbId on matched existing titles.
        var existingTitles = await _dbContext.StudioTitles
            .Where(t => t.StudioId == studio.Id)
            .ToListAsync(cancellationToken);

        var existingTitlesById = existingTitles.ToDictionary(t => t.Id);
        var retainedTitleIds = new HashSet<Guid>();

        for (var i = 0; i < data.Titles.Count; i++)
        {
            var incoming = data.Titles[i];
            var sortOrder = (short)i;

            if (incoming.Id.HasValue && existingTitlesById.TryGetValue(incoming.Id.Value, out var matched))
            {
                retainedTitleIds.Add(matched.Id);
                matched.GameName = incoming.GameName;
                matched.ReleaseYear = incoming.ReleaseYear;
                matched.Platforms = incoming.Platforms;
                matched.StoreUrl = incoming.StoreUrl;
                matched.SortOrder = sortOrder;
            }
            else
            {
                _dbContext.StudioTitles.Add(new StudioTitle
                {
                    Id = Guid.NewGuid(),
                    StudioId = studio.Id,
                    GameName = incoming.GameName,
                    ReleaseYear = incoming.ReleaseYear,
                    Platforms = incoming.Platforms,
                    StoreUrl = incoming.StoreUrl,
                    SortOrder = sortOrder
                });
            }
        }

        foreach (var existingTitle in existingTitles)
        {
            if (!retainedTitleIds.Contains(existingTitle.Id))
            {
                _dbContext.StudioTitles.Remove(existingTitle);
            }
        }

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "studio.profile.updated",
            EntityType: "studio_profile",
            EntityId: studio.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: command.ActorUserId,
            BeforeData: new { Slug = previousSlug },
            AfterData: new
            {
                studio.Name,
                studio.Slug,
                studio.CityId,
                SizeBand = studio.SizeBand.ToDbValue(),
                studio.StudioType,
                SkillCount = data.DistinctSkillIds.Length,
                TitleCount = data.Titles.Count
            }));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Failure<StudioProfileDetail>(
                Error.Conflict("StudioProfile.SlugTaken", $"The URL slug '{data.Slug}' is already in use."));
        }

        return await BuildStudioProfileDetailAsync(studio.Id, callerMember.MemberRole, cancellationToken);
    }

    public async Task<Result<PublicStudioPageModel>> GetPublicStudioBySlugAsync(
        string rawSlug,
        PublicStudioViewer viewer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (string.IsNullOrWhiteSpace(rawSlug))
        {
            return Result.Failure<PublicStudioPageModel>(
                Error.NotFound("PublicStudio.NotFound", "Studio profile was not found."));
        }

        var normalizedLookupSlug = rawSlug.Trim().ToLowerInvariant();

        var studio = await _dbContext.StudioProfiles
            .AsNoTracking()
            .Include(s => s.City)
            .FirstOrDefaultAsync(
                s => s.Slug == normalizedLookupSlug && s.DeletedAt == null,
                cancellationToken);

        if (studio is null)
        {
            return Result.Failure<PublicStudioPageModel>(
                Error.NotFound("PublicStudio.NotFound", "Studio profile was not found."));
        }

        StudioMemberRole? viewerMemberRole = null;
        if (viewer.IsAuthenticated && viewer.UserId.HasValue && viewer.UserId.Value != Guid.Empty)
        {
            var member = await _dbContext.StudioMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.StudioId == studio.Id && m.UserId == viewer.UserId.Value,
                    cancellationToken);

            viewerMemberRole = member?.MemberRole;
        }

        var canView = StudioProfileRules.CanViewPublicStudioPage(
            status: studio.VerificationStatus,
            isDeleted: false,
            isMemberOfStudio: viewerMemberRole.HasValue,
            isAdmin: viewer.IsAuthenticated && viewer.IsAdmin);

        if (!canView)
        {
            return Result.Failure<PublicStudioPageModel>(
                Error.NotFound("PublicStudio.NotPublished", "Studio profile is not publicly published yet."));
        }

        var skillItems = await _dbContext.StudioSkills
            .AsNoTracking()
            .Where(ss => ss.StudioId == studio.Id && ss.Skill.IsActive)
            .OrderBy(ss => ss.Skill.SortOrder)
            .ThenBy(ss => ss.Skill.Name)
            .Select(ss => new StudioSkillItem(
                ss.SkillId,
                ss.Skill.Name,
                ss.Skill.Slug,
                ss.Skill.Category))
            .ToListAsync(cancellationToken);

        var titles = await _dbContext.StudioTitles
            .AsNoTracking()
            .Where(t => t.StudioId == studio.Id)
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.ReleaseYear)
            .Select(t => new StudioTitleItem(
                t.Id,
                t.GameName,
                t.ReleaseYear,
                t.Platforms,
                t.StoreUrl,
                t.IgdbId,
                t.SortOrder))
            .ToListAsync(cancellationToken);

        var isVerified = studio.VerificationStatus == StudioVerificationStatus.Verified;

        return Result.Success(new PublicStudioPageModel(
            StudioId: studio.Id,
            Name: studio.Name,
            Slug: studio.Slug,
            LogoUrl: ResolveLogoUrlOrNull(studio.LogoPath),
            Website: studio.Website,
            Description: studio.Description,
            CityName: studio.City?.Name,
            CityState: studio.City?.State,
            SizeBand: studio.SizeBand,
            StudioType: studio.StudioType,
            FoundedYear: studio.FoundedYear,
            CultureTags: studio.CultureTags,
            VerificationStatus: studio.VerificationStatus,
            IsVerified: isVerified,
            IsPreviewMode: !isVerified,
            ViewerMemberRole: viewerMemberRole,
            Apps90d: studio.Apps90d,
            ResponseRate90d: studio.ResponseRate90d,
            MedianFirstResponseHours: studio.MedianFirstResponseHours,
            Engines: skillItems.Where(s => s.Category == SkillCategory.Engine).ToArray(),
            Genres: skillItems.Where(s => s.Category == SkillCategory.Genre).ToArray(),
            Platforms: skillItems.Where(s => s.Category == SkillCategory.Platform).ToArray(),
            Titles: titles));
    }

    private async Task<Result<string>> ResolveCreationSlugAsync(
        string baseNormalizedSlug,
        bool slugWasExplicit,
        CancellationToken cancellationToken)
    {
        var baseExists = await _dbContext.StudioProfiles
            .AsNoTracking()
            .AnyAsync(s => s.Slug == baseNormalizedSlug, cancellationToken);

        if (!baseExists)
        {
            return Result.Success(baseNormalizedSlug);
        }

        if (slugWasExplicit)
        {
            return Result.Failure<string>(
                Error.Conflict("StudioProfile.SlugTaken", $"The URL slug '{baseNormalizedSlug}' is already in use."));
        }

        for (var suffix = 2; suffix <= 20; suffix++)
        {
            var suffixToken = $"-{suffix}";
            var maxPrefixLen = StudioProfileRules.MaxStudioSlugLength - suffixToken.Length;
            var prefix = baseNormalizedSlug.Length > maxPrefixLen
                ? baseNormalizedSlug[..maxPrefixLen].TrimEnd('-')
                : baseNormalizedSlug;

            var candidate = $"{prefix}{suffixToken}";
            if (!StudioProfileRules.TryNormalizeSlug(candidate, out var validCandidate, out _))
            {
                continue;
            }

            var candidateTaken = await _dbContext.StudioProfiles
                .AsNoTracking()
                .AnyAsync(s => s.Slug == validCandidate, cancellationToken);

            if (!candidateTaken)
            {
                return Result.Success(validCandidate);
            }
        }

        return Result.Failure<string>(
            Error.Conflict(
                "StudioProfile.SlugTaken",
                $"The URL slug '{baseNormalizedSlug}' is already in use. Enter a custom studio URL slug."));
    }

    private async Task<Result> ValidateCityAndSkillsAsync(
        int cityId,
        int[] distinctSkillIds,
        CancellationToken cancellationToken)
    {
        var cityExists = await _dbContext.Cities
            .AsNoTracking()
            .AnyAsync(c => c.Id == cityId && c.IsActive, cancellationToken);

        if (!cityExists)
        {
            return Result.Failure(
                Error.Validation("StudioProfile.InvalidCity", "Selected HQ city does not exist or is inactive."));
        }

        var skills = await _dbContext.Skills
            .AsNoTracking()
            .Where(s => distinctSkillIds.Contains(s.Id) && s.IsActive)
            .Select(s => new { s.Id, s.Name, s.Category })
            .ToListAsync(cancellationToken);

        if (skills.Count != distinctSkillIds.Length)
        {
            return Result.Failure(
                Error.Validation("StudioProfile.InvalidSkill", "One or more selected studio skills do not exist or are inactive."));
        }

        for (var i = 0; i < skills.Count; i++)
        {
            if (!StudioProfileRules.IsAllowedStudioSkillCategory(skills[i].Category))
            {
                return Result.Failure(
                    Error.Validation(
                        "StudioProfile.InvalidSkillCategory",
                        $"Skill '{skills[i].Name}' cannot be attached to a studio stack (only Engines, Genres, and Platforms are allowed)."));
            }
        }

        var engineCount = skills.Count(s => s.Category == SkillCategory.Engine);
        if (engineCount < StudioProfileRules.MinRequiredEngineSkills)
        {
            return Result.Failure(
                Error.Validation("StudioProfile.EngineRequired", "Select at least 1 game engine your studio works with."));
        }

        return Result.Success();
    }

    private async Task<Result<StudioProfileDetail>> BuildStudioProfileDetailAsync(
        Guid studioId,
        StudioMemberRole callerRole,
        CancellationToken cancellationToken)
    {
        var studio = await _dbContext.StudioProfiles
            .AsNoTracking()
            .Include(s => s.City)
            .FirstOrDefaultAsync(s => s.Id == studioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioProfileDetail>(
                Error.NotFound("StudioProfile.NotFound", "Studio profile was not found."));
        }

        var skills = await _dbContext.StudioSkills
            .AsNoTracking()
            .Where(ss => ss.StudioId == studio.Id && ss.Skill.IsActive)
            .OrderBy(ss => ss.Skill.Category)
            .ThenBy(ss => ss.Skill.SortOrder)
            .ThenBy(ss => ss.Skill.Name)
            .Select(ss => new StudioSkillItem(
                ss.SkillId,
                ss.Skill.Name,
                ss.Skill.Slug,
                ss.Skill.Category))
            .ToListAsync(cancellationToken);

        var titles = await _dbContext.StudioTitles
            .AsNoTracking()
            .Where(t => t.StudioId == studio.Id)
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.ReleaseYear)
            .Select(t => new StudioTitleItem(
                t.Id,
                t.GameName,
                t.ReleaseYear,
                t.Platforms,
                t.StoreUrl,
                t.IgdbId,
                t.SortOrder))
            .ToListAsync(cancellationToken);

        var memberCount = await _dbContext.StudioMembers
            .AsNoTracking()
            .CountAsync(m => m.StudioId == studio.Id, cancellationToken);

        return Result.Success(new StudioProfileDetail(
            Id: studio.Id,
            OwnerUserId: studio.OwnerUserId,
            CallerRole: callerRole,
            CanEditProfile: StudioProfileRules.CanEditProfile(callerRole),
            CanEditSlug: StudioProfileRules.CanEditSlug(callerRole, studio.VerificationStatus),
            CanManageTeam: StudioProfileRules.CanManageTeam(callerRole),
            Name: studio.Name,
            Slug: studio.Slug,
            LogoObjectKey: studio.LogoPath,
            LogoUrl: ResolveLogoUrlOrNull(studio.LogoPath),
            Website: studio.Website,
            CompanyEmailDomain: studio.CompanyEmailDomain,
            Description: studio.Description,
            CityId: studio.CityId,
            CityName: studio.City?.Name,
            CityState: studio.City?.State,
            SizeBand: studio.SizeBand,
            StudioType: studio.StudioType,
            FoundedYear: studio.FoundedYear,
            CultureTags: studio.CultureTags,
            Gstin: studio.Gstin,
            VerificationStatus: studio.VerificationStatus,
            VerifiedAt: studio.VerifiedAt,
            VerificationNotes: studio.VerificationNotes,
            Apps90d: studio.Apps90d,
            ResponseRate90d: studio.ResponseRate90d,
            MedianFirstResponseHours: studio.MedianFirstResponseHours,
            Skills: skills,
            Titles: titles,
            MemberCount: memberCount,
            CreatedAt: studio.CreatedAt,
            UpdatedAt: studio.UpdatedAt));
    }

    private async Task EnsureUserHasStudioRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var normalizedStudioRole = HireGameDevRoleNames.Studio.ToUpperInvariant();
        var studioRole = await _dbContext.Roles
            .FirstOrDefaultAsync(r => r.NormalizedName == normalizedStudioRole, cancellationToken);

        if (studioRole is null)
        {
            studioRole = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = HireGameDevRoleNames.Studio,
                NormalizedName = normalizedStudioRole,
                ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            _dbContext.Roles.Add(studioRole);
        }

        var hasRole = await _dbContext.UserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == studioRole.Id, cancellationToken);

        if (!hasRole)
        {
            _dbContext.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = userId,
                RoleId = studioRole.Id
            });
        }
    }

    private string? ResolveLogoUrlOrNull(string? logoObjectKey)
    {
        if (string.IsNullOrWhiteSpace(logoObjectKey))
        {
            return null;
        }

        return ObjectKeyRules.TryNormalize(logoObjectKey, out var normalized, out _)
            ? _fileStorage.ResolvePublicUrl(normalized)
            : null;
    }
}
