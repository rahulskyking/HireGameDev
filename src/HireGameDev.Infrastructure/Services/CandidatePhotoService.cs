using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.Media;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Candidates;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireGameDev.Infrastructure.Services;

/// <summary>
/// Provider-independent candidate avatar workflow (ADR-016).
/// Contains zero local filesystem path references; stores only normalized relative object keys
/// (<c>avatars/&lt;guid&gt;.webp</c>) in <c>candidate_profiles.photo_path</c>.
/// </summary>
public sealed class CandidatePhotoService : ICandidatePhotoService
{
    private readonly AppDbContext _dbContext;
    private readonly IImageProcessor _imageProcessor;
    private readonly IFileStorageService _fileStorage;
    private readonly ICandidateCompletenessCalculator _completenessCalculator;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;
    private readonly ILogger<CandidatePhotoService> _logger;

    public CandidatePhotoService(
        AppDbContext dbContext,
        IImageProcessor imageProcessor,
        IFileStorageService fileStorage,
        ICandidateCompletenessCalculator completenessCalculator,
        IAuditLogger auditLogger,
        IClock clock,
        ILogger<CandidatePhotoService> logger)
    {
        _dbContext = dbContext;
        _imageProcessor = imageProcessor;
        _fileStorage = fileStorage;
        _completenessCalculator = completenessCalculator;
        _auditLogger = auditLogger;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<CandidatePhotoMutationResult>> UploadPhotoAsync(
        UploadCandidatePhotoCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Content);

        if (command.UserId == Guid.Empty)
        {
            return Result.Failure<CandidatePhotoMutationResult>(
                Error.Validation("CandidatePhoto.InvalidUser", "A valid user identity is required."));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(p => p.UserId == command.UserId && p.DeletedAt == null, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CandidatePhotoMutationResult>(
                Error.NotFound(
                    "CandidateProfile.NotFound",
                    "Save Step 1 (Basics) first before uploading a profile photo."));
        }

        var processResult = await _imageProcessor.ProcessAsync(
            new ImageProcessRequest(
                SourceStream: command.Content,
                FileName: command.FileName,
                DeclaredContentType: command.DeclaredContentType,
                Preset: ImageUploadPreset.CandidateAvatar),
            cancellationToken);

        if (processResult.IsFailure)
        {
            return Result.Failure<CandidatePhotoMutationResult>(processResult.Error);
        }

        var processed = processResult.Value;
        var newObjectKey = ObjectKeyRules.BuildRandomWebpKey(ObjectKeyRules.CandidateAvatarsPrefix);

        await using (var payloadStream = new MemoryStream(processed.Content, writable: false))
        {
            await _fileStorage.PutAsync(
                new FileStoragePutRequest(
                    ObjectKey: newObjectKey,
                    Content: payloadStream,
                    ContentType: processed.ContentType,
                    Visibility: StorageVisibility.Private),
                cancellationToken);
        }

        var previousObjectKey = profile.PhotoPath;
        var now = _clock.UtcNow;

        CandidateCompletenessBreakdown breakdown;
        try
        {
            profile.PhotoPath = newObjectKey;
            profile.LastActiveAt = now;

            breakdown = await _completenessCalculator.RecalculateAndApplyAsync(
                profile,
                cancellationToken: cancellationToken);

            _auditLogger.Enqueue(new AuditLogEntry(
                Action: string.IsNullOrWhiteSpace(previousObjectKey)
                    ? "candidate.profile.photo_uploaded"
                    : "candidate.profile.photo_replaced",
                EntityType: "candidate_profile",
                EntityId: profile.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: command.UserId,
                BeforeData: new { PhotoObjectKey = previousObjectKey },
                AfterData: new
                {
                    PhotoObjectKey = newObjectKey,
                    StorageProvider = _fileStorage.ProviderName,
                    Completeness = breakdown.TotalScore
                }));

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await TryDeleteObjectQuietlyAsync(newObjectKey);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(previousObjectKey) &&
            !string.Equals(previousObjectKey, newObjectKey, StringComparison.Ordinal))
        {
            await TryDeleteObjectQuietlyAsync(previousObjectKey);
        }

        return Result.Success(new CandidatePhotoMutationResult(
            CandidateId: profile.Id,
            PhotoObjectKey: profile.PhotoPath,
            Completeness: breakdown.TotalScore,
            UpdatedAt: profile.UpdatedAt));
    }

    public async Task<Result<CandidatePhotoMutationResult>> RemovePhotoAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<CandidatePhotoMutationResult>(
                Error.Validation("CandidatePhoto.InvalidUser", "A valid user identity is required."));
        }

        var profile = await _dbContext.CandidateProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.DeletedAt == null, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CandidatePhotoMutationResult>(
                Error.NotFound("CandidateProfile.NotFound", "Candidate profile was not found."));
        }

        if (string.IsNullOrWhiteSpace(profile.PhotoPath))
        {
            return Result.Success(new CandidatePhotoMutationResult(
                CandidateId: profile.Id,
                PhotoObjectKey: null,
                Completeness: profile.Completeness,
                UpdatedAt: profile.UpdatedAt));
        }

        var previousObjectKey = profile.PhotoPath;
        profile.PhotoPath = null;
        profile.LastActiveAt = _clock.UtcNow;

        var breakdown = await _completenessCalculator.RecalculateAndApplyAsync(
            profile,
            cancellationToken: cancellationToken);

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "candidate.profile.photo_removed",
            EntityType: "candidate_profile",
            EntityId: profile.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: userId,
            BeforeData: new { PhotoObjectKey = previousObjectKey },
            AfterData: new
            {
                PhotoObjectKey = (string?)null,
                Completeness = breakdown.TotalScore
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        await TryDeleteObjectQuietlyAsync(previousObjectKey);

        return Result.Success(new CandidatePhotoMutationResult(
            CandidateId: profile.Id,
            PhotoObjectKey: null,
            Completeness: breakdown.TotalScore,
            UpdatedAt: profile.UpdatedAt));
    }

    public async Task<Result<StoredObjectReadResult>> OpenAuthorizedAvatarAsync(
        Guid candidateId,
        CandidateMediaViewer viewer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (!viewer.IsAuthenticated)
        {
            return Result.Failure<StoredObjectReadResult>(
                Error.Unauthorized("CandidateMedia.AuthRequired", "Sign in to view candidate profile photos."));
        }

        if (candidateId == Guid.Empty)
        {
            return Result.Failure<StoredObjectReadResult>(
                Error.NotFound("CandidateMedia.NotFound", "Candidate profile was not found."));
        }

        var candidate = await _dbContext.CandidateProfiles
            .AsNoTracking()
            .Where(p => p.Id == candidateId)
            .Select(p => new
            {
                p.Id,
                p.UserId,
                p.PhotoPath,
                p.DeletedAt,
                p.IsDiscoverable,
                p.Completeness
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null || candidate.DeletedAt is not null)
        {
            return Result.Failure<StoredObjectReadResult>(
                Error.NotFound("CandidateMedia.NotFound", "Candidate profile was not found."));
        }

        var isOwner = viewer.UserId.HasValue && viewer.UserId.Value == candidate.UserId;
        var isAdmin = viewer.IsAdmin;

        // Authorized Studios can view discoverable profiles (>= 70%) only when the viewer belongs to
        // at least one active Verified studio and zero active studios blocked by the candidate (ADR-020).
        // Phase 3 explicit job-application overrides hook cleanly here alongside discovery checks.
        var isAuthorizedStudio = false;
        if (!isOwner &&
            !isAdmin &&
            viewer.IsStudio &&
            viewer.UserId.HasValue &&
            CandidateProfileRules.IsDiscoverableByStudios(candidate.IsDiscoverable, candidate.Completeness))
        {
            isAuthorizedStudio = await CandidateSettingsService.HasUnblockedVerifiedStudioMembershipAsync(
                _dbContext,
                candidate.Id,
                viewer.UserId.Value,
                cancellationToken);
        }

        if (!isOwner && !isAdmin && !isAuthorizedStudio)
        {
            return Result.Failure<StoredObjectReadResult>(
                Error.Forbidden("CandidateMedia.Forbidden", "You are not authorized to view this candidate photo."));
        }

        if (string.IsNullOrWhiteSpace(candidate.PhotoPath))
        {
            return Result.Failure<StoredObjectReadResult>(
                Error.NotFound("CandidateMedia.NoPhoto", "Candidate has not uploaded a profile photo."));
        }

        var readResult = await _fileStorage.OpenReadAsync(
            candidate.PhotoPath,
            StorageVisibility.Private,
            cancellationToken);

        if (readResult is null)
        {
            return Result.Failure<StoredObjectReadResult>(
                Error.NotFound("CandidateMedia.ObjectMissing", "Stored candidate profile photo could not be found."));
        }

        return Result.Success(readResult);
    }

    private async Task TryDeleteObjectQuietlyAsync(string objectKey)
    {
        try
        {
            await _fileStorage.DeleteIfExistsAsync(objectKey, StorageVisibility.Private, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clean up obsolete candidate avatar object '{ObjectKey}' on provider '{Provider}'.",
                objectKey,
                _fileStorage.ProviderName);
        }
    }
}
