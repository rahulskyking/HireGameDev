using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Media;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Audit;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireGameDev.Infrastructure.Services;

/// <summary>
/// Provider-independent studio logo workflow (ADR-016 &amp; ADR-018).
/// Resizes uploads to &lt;= 256x256 WebP and stores only normalized relative object keys
/// (<c>logos/&lt;guid&gt;.webp</c>) under <see cref="StorageVisibility.Public"/>.
/// </summary>
public sealed class StudioLogoService : IStudioLogoService
{
    private readonly AppDbContext _dbContext;
    private readonly IImageProcessor _imageProcessor;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<StudioLogoService> _logger;

    public StudioLogoService(
        AppDbContext dbContext,
        IImageProcessor imageProcessor,
        IFileStorageService fileStorage,
        IAuditLogger auditLogger,
        ILogger<StudioLogoService> logger)
    {
        _dbContext = dbContext;
        _imageProcessor = imageProcessor;
        _fileStorage = fileStorage;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<Result<StudioLogoMutationResult>> UploadLogoAsync(
        UploadStudioLogoCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Content);

        if (command.ActorUserId == Guid.Empty || command.StudioId == Guid.Empty)
        {
            return Result.Failure<StudioLogoMutationResult>(
                Error.Validation("StudioLogo.InvalidIdentity", "Valid user and studio identifiers are required."));
        }

        var studio = await _dbContext.StudioProfiles
            .FirstOrDefaultAsync(s => s.Id == command.StudioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioLogoMutationResult>(
                Error.NotFound("StudioProfile.NotFound", "Save your studio profile basics first before uploading a logo."));
        }

        var member = await _dbContext.StudioMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == command.ActorUserId,
                cancellationToken);

        if (member is null || !StudioProfileRules.CanUploadLogo(member.MemberRole))
        {
            return Result.Failure<StudioLogoMutationResult>(
                Error.Forbidden(
                    "StudioLogo.Forbidden",
                    "Only the studio Owner or a Recruiter can upload or change the studio logo."));
        }

        var processResult = await _imageProcessor.ProcessAsync(
            new ImageProcessRequest(
                SourceStream: command.Content,
                FileName: command.FileName,
                DeclaredContentType: command.DeclaredContentType,
                Preset: ImageUploadPreset.StudioLogo),
            cancellationToken);

        if (processResult.IsFailure)
        {
            return Result.Failure<StudioLogoMutationResult>(processResult.Error);
        }

        var processed = processResult.Value;
        var newObjectKey = ObjectKeyRules.BuildRandomWebpKey(ObjectKeyRules.StudioLogosPrefix);

        await using (var payloadStream = new MemoryStream(processed.Content, writable: false))
        {
            await _fileStorage.PutAsync(
                new FileStoragePutRequest(
                    ObjectKey: newObjectKey,
                    Content: payloadStream,
                    ContentType: processed.ContentType,
                    Visibility: StorageVisibility.Public,
                    CacheControl: "public, max-age=31536000, immutable"),
                cancellationToken);
        }

        var previousObjectKey = studio.LogoPath;
        try
        {
            studio.LogoPath = newObjectKey;

            _auditLogger.Enqueue(new AuditLogEntry(
                Action: string.IsNullOrWhiteSpace(previousObjectKey)
                    ? "studio.profile.logo_uploaded"
                    : "studio.profile.logo_replaced",
                EntityType: "studio_profile",
                EntityId: studio.Id.ToString(),
                ActorType: AuditActorType.User,
                ActorUserId: command.ActorUserId,
                BeforeData: new { LogoObjectKey = previousObjectKey },
                AfterData: new
                {
                    LogoObjectKey = newObjectKey,
                    StorageProvider = _fileStorage.ProviderName,
                    Width = processed.Width,
                    Height = processed.Height
                }));

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await TryDeletePublicObjectQuietlyAsync(newObjectKey);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(previousObjectKey) &&
            !string.Equals(previousObjectKey, newObjectKey, StringComparison.Ordinal))
        {
            await TryDeletePublicObjectQuietlyAsync(previousObjectKey);
        }

        return Result.Success(new StudioLogoMutationResult(
            StudioId: studio.Id,
            LogoObjectKey: studio.LogoPath,
            LogoUrl: _fileStorage.ResolvePublicUrl(newObjectKey),
            UpdatedAt: studio.UpdatedAt));
    }

    public async Task<Result<StudioLogoMutationResult>> RemoveLogoAsync(
        Guid actorUserId,
        Guid studioId,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || studioId == Guid.Empty)
        {
            return Result.Failure<StudioLogoMutationResult>(
                Error.Validation("StudioLogo.InvalidIdentity", "Valid user and studio identifiers are required."));
        }

        var studio = await _dbContext.StudioProfiles
            .FirstOrDefaultAsync(s => s.Id == studioId && s.DeletedAt == null, cancellationToken);

        if (studio is null)
        {
            return Result.Failure<StudioLogoMutationResult>(
                Error.NotFound("StudioProfile.NotFound", "Studio profile was not found."));
        }

        var member = await _dbContext.StudioMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StudioId == studio.Id && m.UserId == actorUserId,
                cancellationToken);

        if (member is null || !StudioProfileRules.CanUploadLogo(member.MemberRole))
        {
            return Result.Failure<StudioLogoMutationResult>(
                Error.Forbidden(
                    "StudioLogo.Forbidden",
                    "Only the studio Owner or a Recruiter can remove the studio logo."));
        }

        if (string.IsNullOrWhiteSpace(studio.LogoPath))
        {
            return Result.Success(new StudioLogoMutationResult(
                StudioId: studio.Id,
                LogoObjectKey: null,
                LogoUrl: null,
                UpdatedAt: studio.UpdatedAt));
        }

        var previousObjectKey = studio.LogoPath;
        studio.LogoPath = null;

        _auditLogger.Enqueue(new AuditLogEntry(
            Action: "studio.profile.logo_removed",
            EntityType: "studio_profile",
            EntityId: studio.Id.ToString(),
            ActorType: AuditActorType.User,
            ActorUserId: actorUserId,
            BeforeData: new { LogoObjectKey = previousObjectKey },
            AfterData: new { LogoObjectKey = (string?)null }));

        await _dbContext.SaveChangesAsync(cancellationToken);

        await TryDeletePublicObjectQuietlyAsync(previousObjectKey);

        return Result.Success(new StudioLogoMutationResult(
            StudioId: studio.Id,
            LogoObjectKey: null,
            LogoUrl: null,
            UpdatedAt: studio.UpdatedAt));
    }

    private async Task TryDeletePublicObjectQuietlyAsync(string objectKey)
    {
        try
        {
            await _fileStorage.DeleteIfExistsAsync(objectKey, StorageVisibility.Public, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clean up obsolete studio logo object '{ObjectKey}' on provider '{Provider}'.",
                objectKey,
                _fileStorage.ProviderName);
        }
    }
}
