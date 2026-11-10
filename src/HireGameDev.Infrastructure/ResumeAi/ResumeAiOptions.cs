namespace HireGameDev.Infrastructure.ResumeAi;

public sealed class ResumeAiOptions
{
    public const string SectionName = "ResumeAi";

    /// <summary>
    /// Active LLM adapter: <c>"Dev"</c> (deterministic local extractor for offline dev/testing per ADR-013),
    /// or paid zero-training production providers (<c>"Gemini"</c> / <c>"OpenAI"</c>) enabled pre-launch.
    /// </summary>
    public string Provider { get; set; } = "Dev";

    public int TimeoutSeconds { get; set; } = 25;

    public PaidLlmProviderOptions Gemini { get; set; } = new()
    {
        Model = "gemini-2.5-flash",
        Endpoint = "https://generativelanguage.googleapis.com/"
    };

    public PaidLlmProviderOptions OpenAi { get; set; } = new()
    {
        Model = "gpt-4o-mini",
        Endpoint = "https://api.openai.com/v1/"
    };
}

public sealed class PaidLlmProviderOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Baseline Principle (Blueprint Section 13.F): Never send resume content to free/training-enabled tiers.
    /// </summary>
    public bool PaidNoTrainingTierConfirmed { get; set; }
}
