using System.Net.Http.Json;
using System.Text.Json;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Common;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.ResumeAi;

/// <summary>
/// Paid, zero-training Google Gemini structured JSON adapter (<c>temperature = 0</c>, <c>responseMimeType = "application/json"</c>).
/// Sends the API key strictly via the <c>x-goog-api-key</c> header (never in URLs/logs) and never logs resume or completion content.
/// </summary>
internal sealed class GeminiResumeLlmClient : IResumeLlmClient
{
    private readonly HttpClient _httpClient;
    private readonly ResumeAiOptions _options;

    public GeminiResumeLlmClient(
        HttpClient httpClient,
        IOptions<ResumeAiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string ProviderName => "Gemini";

    public async Task<Result<string>> ExtractResumeJsonAsync(
        string redactedResumeText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(redactedResumeText))
        {
            return Result.Failure<string>(
                Error.Validation("ResumeImport.EmptyRedactedText", "No readable resume text remained after PII redaction."));
        }

        var cfg = _options.Gemini;
        if (!cfg.PaidNoTrainingTierConfirmed || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            return Result.Failure<string>(
                Error.Validation(
                    "ResumeImport.ProviderNotConfigured",
                    "Gemini resume extraction requires a configured paid API key and PaidNoTrainingTierConfirmed=true."));
        }

        var model = string.IsNullOrWhiteSpace(cfg.Model) ? "gemini-2.5-flash" : cfg.Model.Trim();
        var baseEndpoint = string.IsNullOrWhiteSpace(cfg.Endpoint)
            ? "https://generativelanguage.googleapis.com/"
            : cfg.Endpoint.Trim();
        if (!baseEndpoint.EndsWith('/'))
        {
            baseEndpoint += "/";
        }

        var requestUri = new Uri(
            new Uri(baseEndpoint, UriKind.Absolute),
            $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent");

        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = ResumeLlmPromptBuilder.SystemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = ResumeLlmPromptBuilder.BuildUserPrompt(redactedResumeText) } }
                }
            },
            generationConfig = new
            {
                temperature = 0,
                responseMimeType = "application/json"
            }
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 90)));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.TryAddWithoutValidation("x-goog-api-key", cfg.ApiKey.Trim());

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<string>(
                    Error.Validation(
                        $"ResumeImport.GeminiHttp{(int)response.StatusCode}",
                        "Gemini extraction service returned a non-success status."));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textProp) &&
                textProp.ValueKind == JsonValueKind.String)
            {
                var json = textProp.GetString();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    return Result.Success(json);
                }
            }

            return Result.Failure<string>(
                Error.Validation("ResumeImport.EmptyLlmOutput", "Gemini returned an empty extraction payload."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<string>(
                Error.Validation("ResumeImport.LlmTimeout", "Resume extraction timed out. Please try again."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Result.Failure<string>(
                Error.Validation("ResumeImport.LlmNetworkError", "Could not reach the Gemini extraction endpoint."));
        }
    }
}
