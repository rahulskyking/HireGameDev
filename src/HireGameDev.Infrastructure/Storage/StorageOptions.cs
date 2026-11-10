namespace HireGameDev.Infrastructure.Storage;

/// <summary>
/// Configuration for provider-independent object storage (ADR-016).
/// Supports <c>Local</c> storage outside <c>wwwroot</c> now and clean config-driven cutover
/// to S3-compatible <c>CloudflareR2</c> later without touching feature services or PostgreSQL keys.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Active storage backend: <c>Local</c> (default) or <c>CloudflareR2</c> (future S3-compatible adapter).
    /// </summary>
    public string Provider { get; set; } = "Local";

    public LocalStorageOptions Local { get; set; } = new();

    public CloudflareR2StorageOptions CloudflareR2 { get; set; } = new();
}

public sealed class LocalStorageOptions
{
    /// <summary>
    /// Directory outside <c>wwwroot</c> holding <c>private/</c> and <c>public/</c> object trees.
    /// Relative paths resolve against <c>ContentRootPath</c>; production Linux deployments use an
    /// absolute path such as <c>/var/lib/hiregamedev/uploads</c>.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/uploads";

    /// <summary>
    /// Route prefix used when resolving URLs for <c>Public</c> objects (e.g., studio logos in M1.9) on Local storage.
    /// </summary>
    public string PublicRoutePrefix { get; set; } = "/Media/Public";
}

/// <summary>
/// Reserved S3-compatible configuration shape for future <c>CloudflareR2StorageService</c> migration (ADR-016).
/// Not wired in M1.6.
/// </summary>
public sealed class CloudflareR2StorageOptions
{
    public string AccountId { get; set; } = string.Empty;

    public string ServiceUrl { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string PrivateBucket { get; set; } = string.Empty;

    public string PublicBucket { get; set; } = string.Empty;

    public string PublicBaseUrl { get; set; } = string.Empty;
}
