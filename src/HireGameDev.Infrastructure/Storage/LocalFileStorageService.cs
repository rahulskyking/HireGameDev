using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Domain.Rules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.Storage;

/// <summary>
/// Local-disk implementation of <see cref="IFileStorageService"/> storing objects strictly outside <c>wwwroot</c>
/// in isolated <c>private/</c> and <c>public/</c> trees with path-containment guards and atomic temp-file writes (ADR-016).
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private const int StreamBufferBytes = 65_536;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly string _privateRoot;
    private readonly string _publicRoot;
    private readonly string _publicRoutePrefix;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IOptions<StorageOptions> options,
        IHostEnvironment environment,
        ILogger<LocalFileStorageService> logger)
        : this(
            options?.Value ?? throw new ArgumentNullException(nameof(options)),
            environment?.ContentRootPath ?? throw new ArgumentNullException(nameof(environment)),
            (environment as IWebHostEnvironment)?.WebRootPath,
            logger)
    {
    }

    internal LocalFileStorageService(
        StorageOptions options,
        string contentRootPath,
        string? webRootPath,
        ILogger<LocalFileStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        var configuredRoot = string.IsNullOrWhiteSpace(options.Local.RootPath)
            ? "App_Data/uploads"
            : options.Local.RootPath.Trim();

        var resolvedRoot = Path.IsPathRooted(configuredRoot)
            ? Path.GetFullPath(configuredRoot)
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredRoot));

        resolvedRoot = TrimTrailingDirectorySeparator(resolvedRoot);

        EnsureOutsideWebRoot(resolvedRoot, Path.Combine(contentRootPath, "wwwroot"));
        if (!string.IsNullOrWhiteSpace(webRootPath))
        {
            EnsureOutsideWebRoot(resolvedRoot, webRootPath);
        }

        _privateRoot = TrimTrailingDirectorySeparator(Path.GetFullPath(Path.Combine(resolvedRoot, "private")));
        _publicRoot = TrimTrailingDirectorySeparator(Path.GetFullPath(Path.Combine(resolvedRoot, "public")));

        var rawPrefix = string.IsNullOrWhiteSpace(options.Local.PublicRoutePrefix)
            ? "/Media/Public"
            : options.Local.PublicRoutePrefix.Trim();

        _publicRoutePrefix = "/" + rawPrefix.Trim('/');
    }

    public string ProviderName => "Local";

    public async Task PutAsync(
        FileStoragePutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);

        var targetPath = ResolveSafePathOrThrow(request.ObjectKey, request.Visibility);
        var parentDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Unable to determine parent directory for stored object.");

        Directory.CreateDirectory(parentDirectory);

        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var fileStream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                StreamBufferBytes,
                FileOptions.Asynchronous))
            {
                await request.Content.CopyToAsync(fileStream, StreamBufferBytes, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    public Task<StoredObjectReadResult?> OpenReadAsync(
        string objectKey,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveSafePath(objectKey, visibility, out var targetPath))
        {
            _logger.LogWarning("Rejected invalid object key '{ObjectKey}' during OpenReadAsync.", objectKey);
            return Task.FromResult<StoredObjectReadResult?>(null);
        }

        var fileInfo = new FileInfo(targetPath);
        if (!fileInfo.Exists)
        {
            return Task.FromResult<StoredObjectReadResult?>(null);
        }

        FileStream stream;
        try
        {
            stream = new FileStream(
                targetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<StoredObjectReadResult?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<StoredObjectReadResult?>(null);
        }

        var lastModifiedUtc = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
        var etag = $"\"{fileInfo.Length:x}-{fileInfo.LastWriteTimeUtc.Ticks:x}\"";
        var contentType = InferContentType(targetPath);

        return Task.FromResult<StoredObjectReadResult?>(
            new StoredObjectReadResult(
                Content: stream,
                ContentType: contentType,
                ContentLength: fileInfo.Length,
                ETag: etag,
                LastModifiedUtc: lastModifiedUtc));
    }

    public Task<bool> DeleteIfExistsAsync(
        string objectKey,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveSafePath(objectKey, visibility, out var targetPath))
        {
            _logger.LogWarning("Rejected invalid object key '{ObjectKey}' during DeleteIfExistsAsync.", objectKey);
            return Task.FromResult(false);
        }

        if (!File.Exists(targetPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            File.Delete(targetPath);
            return Task.FromResult(true);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult(false);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult(false);
        }
    }

    public string ResolvePublicUrl(string objectKey)
    {
        if (!ObjectKeyRules.TryNormalize(objectKey, out var normalizedKey, out var errorMessage))
        {
            throw new ArgumentException(errorMessage ?? "Invalid object key.", nameof(objectKey));
        }

        return $"{_publicRoutePrefix}/{normalizedKey}";
    }

    private string ResolveSafePathOrThrow(string objectKey, StorageVisibility visibility)
    {
        if (!TryResolveSafePath(objectKey, visibility, out var targetPath))
        {
            throw new ArgumentException(
                "Object key must be a normalized relative key such as 'avatars/<id>.webp'.",
                nameof(objectKey));
        }

        return targetPath;
    }

    private bool TryResolveSafePath(string? objectKey, StorageVisibility visibility, out string fullPath)
    {
        fullPath = string.Empty;

        if (!ObjectKeyRules.TryNormalize(objectKey, out var normalizedKey, out _))
        {
            return false;
        }

        var visibilityRoot = visibility == StorageVisibility.Public ? _publicRoot : _privateRoot;
        var osRelativePath = normalizedKey.Replace('/', Path.DirectorySeparatorChar);
        var candidateFullPath = Path.GetFullPath(Path.Combine(visibilityRoot, osRelativePath));

        var requiredPrefix = visibilityRoot + Path.DirectorySeparatorChar;
        if (!candidateFullPath.StartsWith(requiredPrefix, PathComparison))
        {
            return false;
        }

        fullPath = candidateFullPath;
        return true;
    }

    private static void EnsureOutsideWebRoot(string resolvedStorageRoot, string candidateWebRoot)
    {
        var fullWebRoot = TrimTrailingDirectorySeparator(Path.GetFullPath(candidateWebRoot));
        if (string.Equals(resolvedStorageRoot, fullWebRoot, PathComparison) ||
            resolvedStorageRoot.StartsWith(fullWebRoot + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidOperationException(
                $"Storage:Local:RootPath ('{resolvedStorageRoot}') must be outside wwwroot ('{fullWebRoot}').");
        }
    }

    private static string TrimTrailingDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string InferContentType(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best-effort cleanup of incomplete temporary write.
        }
    }
}
