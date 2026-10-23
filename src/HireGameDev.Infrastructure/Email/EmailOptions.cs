namespace HireGameDev.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Supported values: "Dev" (writes HTML previews to disk) or "Resend" (Resend REST API).
    /// Additional providers (Brevo, ZeptoMail, SES) can be added behind IEmailSender without changing callers.
    /// </summary>
    public string Provider { get; init; } = "Dev";

    public string FromAddress { get; init; } = "noreply@hiregamedev.in";

    public string FromName { get; init; } = "HireGameDev";

    public string ResendApiKey { get; init; } = string.Empty;

    public string DevOutputDirectory { get; init; } = "logs/emails";
}
