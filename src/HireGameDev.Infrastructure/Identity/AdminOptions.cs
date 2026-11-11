namespace HireGameDev.Infrastructure.Identity;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Case-insensitive allowlist of verified user email addresses that should be granted
    /// the 'admin' Identity role at startup or upon verified sign-in (ADR-019).
    /// </summary>
    public string[] BootstrapEmails { get; set; } = Array.Empty<string>();
}
