using System.Text;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.Email;

/// <summary>
/// Local development email sender. Writes clean HTML email previews to `logs/emails/`
/// without logging raw tokens to application log files (Blueprint Section 3 security rule).
/// </summary>
internal sealed class DevEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly IClock _clock;
    private readonly ILogger<DevEmailSender> _logger;

    public DevEmailSender(
        IOptions<EmailOptions> options,
        IHostEnvironment environment,
        IClock clock,
        ILogger<DevEmailSender> logger)
    {
        _options = options.Value;
        _environment = environment;
        _clock = clock;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var outputDir = Path.IsPathRooted(_options.DevOutputDirectory)
            ? _options.DevOutputDirectory
            : Path.Combine(_environment.ContentRootPath, _options.DevOutputDirectory);

        Directory.CreateDirectory(outputDir);

        var safeTag = SanitizeFileSegment(message.Tag ?? "email");
        var timestamp = _clock.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var fileName = $"{timestamp}_{safeTag}_{Guid.NewGuid():N}.html";
        var filePath = Path.Combine(outputDir, fileName);

        var previewDocument = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <title>{System.Net.WebUtility.HtmlEncode(message.Subject)}</title>
            </head>
            <body style="font-family: system-ui, sans-serif; max-width: 680px; margin: 24px auto; padding: 16px;">
              <div style="background: #f6f6f3; border: 1px solid #e2e1db; border-radius: 8px; padding: 12px 16px; margin-bottom: 20px; font-size: 14px;">
                <div><strong>From:</strong> {System.Net.WebUtility.HtmlEncode(_options.FromName)} &lt;{System.Net.WebUtility.HtmlEncode(_options.FromAddress)}&gt;</div>
                <div><strong>To:</strong> {System.Net.WebUtility.HtmlEncode(message.To)}</div>
                <div><strong>Subject:</strong> {System.Net.WebUtility.HtmlEncode(message.Subject)}</div>
                <div><strong>Tag:</strong> {System.Net.WebUtility.HtmlEncode(message.Tag ?? "none")}</div>
              </div>
              <div>
                {message.HtmlBody}
              </div>
            </body>
            </html>
            """;

        await File.WriteAllTextAsync(filePath, previewDocument, Encoding.UTF8, cancellationToken);

        _logger.LogInformation(
            "DevEmailSender wrote email preview ({Tag}, Subject: {Subject}) to {FilePath}",
            message.Tag ?? "email",
            message.Subject,
            filePath);
    }

    private static string SanitizeFileSegment(string input)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(input.Length);

        foreach (var ch in input)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.Length > 0 ? builder.ToString() : "email";
    }
}
