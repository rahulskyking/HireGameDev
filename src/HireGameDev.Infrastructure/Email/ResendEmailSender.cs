using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HireGameDev.Application.Abstractions.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.Email;

/// <summary>
/// Production transactional email adapter using Resend's HTTPS REST API (`POST https://api.resend.com/emails`).
/// Built on standard HttpClient so switching to another provider (Brevo, ZeptoMail, SES) only swaps the DI adapter.
/// </summary>
internal sealed class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly EmailOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        HttpClient httpClient,
        IOptions<EmailOptions> options,
        ILogger<ResendEmailSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(_options.ResendApiKey))
        {
            throw new InvalidOperationException(
                "Email:Provider is set to 'Resend', but 'Email:ResendApiKey' is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);

        var fromHeader = string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.FromAddress
            : $"{_options.FromName} <{_options.FromAddress}>";

        var payload = new ResendSendEmailRequest(
            From: fromHeader,
            To: [message.To],
            Subject: message.Subject,
            Html: message.HtmlBody,
            Text: message.TextBody);

        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Resend API failed with HTTP {StatusCode} for email tag '{Tag}': {ErrorBody}",
                (int)response.StatusCode,
                message.Tag ?? "none",
                errorBody);

            throw new HttpRequestException(
                $"Resend email delivery failed with status code {(int)response.StatusCode}.");
        }

        _logger.LogInformation(
            "ResendEmailSender delivered email ({Tag}, Subject: {Subject})",
            message.Tag ?? "none",
            message.Subject);
    }

    private sealed record ResendSendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text);
}
