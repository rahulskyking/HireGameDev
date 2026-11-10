using HireGameDev.Application.Common;

namespace HireGameDev.Application.Abstractions.Candidates;

/// <summary>
/// Provider-agnostic LLM adapter for structured resume extraction (ADR-013 &amp; Blueprint v2.0 Section 5).
/// Receives ONLY PII-redacted text (<see cref="Domain.Rules.ResumePiiRedactor"/>) and MUST run on a paid,
/// zero-data-retention / no-training tier in production with <c>temperature = 0</c>.
/// </summary>
public interface IResumeLlmClient
{
    string ProviderName { get; }

    Task<Result<string>> ExtractResumeJsonAsync(
        string redactedResumeText,
        CancellationToken cancellationToken = default);
}

public static class ResumeLlmPrompts
{
    /// <summary>
    /// Exact deterministic extraction prompt required by Blueprint v2.0 Section 5 Step 4.
    /// </summary>
    public const string SystemPrompt =
        """
        You are a structured resume parser for video game industry professionals.
        Extract only what is stated in the text, never infer skills, return null for unknown fields, return JSON only.
        Output strictly a single valid JSON object matching this exact schema:
        {
          "headline": "string or null",
          "years_experience": number or null,
          "skills": [{"name": "string", "years_used": number or null, "last_used_year": integer or null}],
          "experiences": [{"company": "string", "title": "string", "start": "YYYY-MM", "end": "YYYY-MM or null", "description": "string or null"}],
          "education": [{"institution": "string", "degree": "string", "field": "string or null", "start_year": integer or null, "end_year": integer or null}],
          "shipped_titles": [{"game": "string", "studio": "string or null", "role": "string or null", "year": integer or null}]
        }
        """;
}
