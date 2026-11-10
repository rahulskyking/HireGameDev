using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Common;
using Microsoft.Extensions.Options;

namespace HireGameDev.Infrastructure.ResumeAi;

/// <summary>
/// Paid, zero-retention OpenAI (or OpenAI-compatible) structured JSON adapter (<c>temperature = 0</c>, <c>store = false</c>).
/// Never logs prompt or completion text.
/// </summary>
internal sealed class OpenAiResumeLlmClient : IResumeLlmClient
{
    private readonly HttpClient _httpClient;
    private readonly ResumeAiOptions _options;

    public OpenAiResumeLlmClient(
        HttpClient httpClient,
        IOptions<ResumeAiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string ProviderName => "OpenAI";

    public async Task<Result<string>> ExtractResumeJsonAsync(
        string redactedResumeText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(redactedResumeText))
        {
            return Result.Failure<string>(
                Error.Validation("ResumeImport.EmptyRedactedText", "No readable resume text remained after PII redaction."));
        }

        var cfg = _options.OpenAi;
        if (!cfg.PaidNoTrainingTierConfirmed || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            return Result.Failure<string>(
                Error.Validation(
                    "ResumeImport.ProviderNotConfigured",
                    "OpenAI resume extraction requires a configured paid API key and PaidNoTrainingTierConfirmed=true."));
        }

        var model = string.IsNullOrWhiteSpace(cfg.Model) ? "gpt-4o-mini" : cfg.Model.Trim();
        var baseEndpoint = string.IsNullOrWhiteSpace(cfg.Endpoint)
            ? "https://api.openai.com/v1/"
            : cfg.Endpoint.Trim();
        if (!baseEndpoint.EndsWith('/'))
        {
            baseEndpoint += "/";
        }

        var requestUri = new Uri(new Uri(baseEndpoint, UriKind.Absolute), "chat/completions");

        var requestBody = new
        {
            model,
            temperature = 0,
            store = false,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = ResumeLlmPromptBuilder.SystemInstruction },
                new { role = "user", content = ResumeLlmPromptBuilder.BuildUserPrompt(redactedResumeText) }
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
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey.Trim());

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<string>(
                    Error.Validation(
                        $"ResumeImport.OpenAiHttp{(int)response.StatusCode}",
                        "OpenAI extraction service returned a non-success status."));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentProp) &&
                contentProp.ValueKind == JsonValueKind.String)
            {
                var json = contentProp.GetString();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    return Result.Success(json);
                }
            }

            return Result.Failure<string>(
                Error.Validation("ResumeImport.EmptyLlmOutput", "OpenAI returned an empty extraction payload."));
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
                Error.Validation("ResumeImport.LlmNetworkError", "Could not reach the OpenAI extraction endpoint."));
        }
    }
}
