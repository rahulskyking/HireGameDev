namespace HireGameDev.Application.Abstractions.Storage;

/// <summary>
/// Distinguishes access-controlled objects (such as candidate avatars served exclusively
/// through authorized app endpoints) from publicly addressable assets (such as verified studio logos).
/// Maps cleanly to isolated local folders now and separate private/public Cloudflare R2 buckets later.
/// </summary>
public enum StorageVisibility
{
    Private = 0,
    Public = 1
}

public sealed record FileStoragePutRequest(
    string ObjectKey,
    Stream Content,
    string ContentType,
    StorageVisibility Visibility,
    string? CacheControl = null);

public sealed record StoredObjectReadResult(
    Stream Content,
    string ContentType,
    long? ContentLength,
    string? ETag,
    DateTimeOffset? LastModifiedUtc) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// Provider-independent object storage contract (ADR-016).
/// Callers pass and persist only normalized relative object keys (e.g. <c>avatars/&lt;guid&gt;.webp</c>),
/// never local filesystem paths or provider-specific bucket URLs.
/// </summary>
public interface IFileStorageService
{
    string ProviderName { get; }

    Task PutAsync(
        FileStoragePutRequest request,
        CancellationToken cancellationToken = default);

    Task<StoredObjectReadResult?> OpenReadAsync(
        string objectKey,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteIfExistsAsync(
        string objectKey,
        StorageVisibility visibility,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the browser-facing URL for a <see cref="StorageVisibility.Public"/> object key
    /// (for example, <c>/Media/Public/logos/...</c> on Local storage or a CDN custom domain on Cloudflare R2).
    /// Do not use for <see cref="StorageVisibility.Private"/> candidate avatars.
    /// </summary>
    string ResolvePublicUrl(string objectKey);
}
