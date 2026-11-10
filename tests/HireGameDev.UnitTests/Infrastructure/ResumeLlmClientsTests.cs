using System.Net;
using System.Text;
using HireGameDev.Infrastructure.ResumeAi;
using Microsoft.Extensions.Options;
using Xunit;

namespace HireGameDev.UnitTests.Infrastructure;

public sealed class ResumeLlmClientsTests
{
    [Fact]
    public async Task GeminiAndOpenAiClients_BlockRequests_WhenPaidNoTrainingTierIsNotConfirmed()
    {
        var unconfirmedOptions = Options.Create(new ResumeAiOptions
        {
            Gemini = new PaidLlmProviderOptions
            {
                ApiKey = "test-key",
                PaidNoTrainingTierConfirmed = false
            },
            OpenAi = new PaidLlmProviderOptions
            {
                ApiKey = "test-key",
                PaidNoTrainingTierConfirmed = false
            }
        });

        var handler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);

        var gemini = new GeminiResumeLlmClient(httpClient, unconfirmedOptions);
        var geminiRes = await gemini.ExtractResumeJsonAsync("Senior Unity Gameplay Programmer");
        Assert.True(geminiRes.IsFailure);
        Assert.Equal("ResumeImport.ProviderNotConfigured", geminiRes.Error.Code);

        var openAi = new OpenAiResumeLlmClient(httpClient, unconfirmedOptions);
        var openAiRes = await openAi.ExtractResumeJsonAsync("Senior Unity Gameplay Programmer");
        Assert.True(openAiRes.IsFailure);
        Assert.Equal("ResumeImport.ProviderNotConfigured", openAiRes.Error.Code);

        // Zero outbound network calls may occur unless paid zero-training tier is explicitly confirmed!
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GeminiResumeLlmClient_SendsApiKeyInHeader_TemperatureZero_AndExtractsCandidateJson()
    {
        var options = Options.Create(new ResumeAiOptions
        {
            Provider = "Gemini",
            Gemini = new PaidLlmProviderOptions
            {
                ApiKey = "gem-secret-123",
                Model = "gemini-2.5-flash",
                Endpoint = "https://generativelanguage.googleapis.com/",
                PaidNoTrainingTierConfirmed = true
            }
        });

        const string geminiApiResponse =
            """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "{\"headline\":\"Unreal C++ Gameplay Programmer\",\"years_experience\":4.5,\"skills\":[],\"experiences\":[],\"education\":[],\"shipped_titles\":[]}" }
                    ]
                  }
                }
              ]
            }
            """;

        var handler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(geminiApiResponse, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler);
        var client = new GeminiResumeLlmClient(httpClient, options);

        var result = await client.ExtractResumeJsonAsync("Unreal C++ Gameplay Programmer");

        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Contains("Unreal C++ Gameplay Programmer", result.Value, StringComparison.Ordinal);
        Assert.NotNull(handler.LastRequestUri);
        Assert.DoesNotContain("gem-secret-123", handler.LastRequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal("gem-secret-123", handler.LastGoogApiKeyHeader);
        Assert.Contains("\"temperature\":0", handler.LastRequestBody ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("\"responseMimeType\":\"application/json\"", handler.LastRequestBody ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiResumeLlmClient_SendsStoreFalse_JsonObjectFormat_AndExtractsChoiceContent()
    {
        var options = Options.Create(new ResumeAiOptions
        {
            Provider = "OpenAI",
            OpenAi = new PaidLlmProviderOptions
            {
                ApiKey = "sk-test-456",
                Model = "gpt-4o-mini",
                Endpoint = "https://api.openai.com/v1/",
                PaidNoTrainingTierConfirmed = true
            }
        });

        const string openAiApiResponse =
            """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"headline\":\"Technical Artist\",\"years_experience\":3.0,\"skills\":[],\"experiences\":[],\"education\":[],\"shipped_titles\":[]}"
                  }
                }
              ]
            }
            """;

        var handler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(openAiApiResponse, Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler);
        var client = new OpenAiResumeLlmClient(httpClient, options);

        var result = await client.ExtractResumeJsonAsync("Technical Artist");

        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Contains("Technical Artist", result.Value, StringComparison.Ordinal);
        Assert.Equal("Bearer sk-test-456", handler.LastAuthorizationHeader);
        Assert.Contains("\"store\":false", handler.LastRequestBody ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("\"temperature\":0", handler.LastRequestBody ?? string.Empty, StringComparison.Ordinal);
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int CallCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastGoogApiKeyHeader { get; private set; }
        public string? LastAuthorizationHeader { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastGoogApiKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var gVals)
                ? gVals.FirstOrDefault()
                : null;
            LastAuthorizationHeader = request.Headers.Authorization?.ToString();
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responder(request);
        }
    }
}
