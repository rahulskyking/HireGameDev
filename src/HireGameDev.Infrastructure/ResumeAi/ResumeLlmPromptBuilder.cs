namespace HireGameDev.Infrastructure.ResumeAi;

internal static class ResumeLlmPromptBuilder
{
    public const string SystemInstruction =
        """
        You are a deterministic game-industry resume parser for HireGameDev (India's game developer hiring platform).
        The input text comes from a candidate's PDF resume or LinkedIn "Save to PDF" export with PII placeholders ([EMAIL_REDACTED], [PHONE_REDACTED], [URL_REDACTED]).

        STRICT EXTRACTION RULES:
        1. Extract ONLY facts stated in the resume text. NEVER invent companies, degrees, games, or unmentioned skills.
        2. Never include [EMAIL_REDACTED], [PHONE_REDACTED], or [URL_REDACTED] in any output field.
        3. Return ONLY a single valid JSON object matching this exact schema (use null when unknown, [] when empty):
        {
          "full_name": "string or null (candidate's name from header, 2..80 chars)",
          "headline": "string or null (concise game-dev role headline, 3..80 chars, e.g. 'Senior Unreal C++ Gameplay Programmer')",
          "bio": "string or null (concise professional summary from Summary/About section, max 1000 chars)",
          "primary_discipline": "'programming' | 'art' | 'design' | 'audio' | 'qa' | 'production' | null",
          "city": "string or null (candidate's current city if stated, e.g. 'Bengaluru', 'Pune', 'Hyderabad')",
          "years_experience": "number or null (stated or summed non-overlapping professional years in 0.5 increments, 0..50)",
          "skills": [
            {
              "name": "string (canonical tool/engine/language/genre/skill explicitly mentioned, e.g. 'Unreal Engine', 'Unity', 'C++', 'C#', 'Blender', 'HLSL', 'Wwise')",
              "years_used": "number or null (0.5 increments if stated or clear from dated roles)",
              "last_used_year": "integer YYYY or null",
              "is_primary": "boolean (true for up to 5 core skills central to headline/recent roles)"
            }
          ],
          "experiences": [
            {
              "company": "string (studio or employer name, never tenure text like '3 years 2 months')",
              "title": "string (job title/role)",
              "start": "string ('YYYY-MM' or 'YYYY')",
              "end": "string or null ('YYYY-MM', 'YYYY', or null if current/Present)",
              "description": "string or null (clean bullet summary of responsibilities & tech used, max 1500 chars)"
            }
          ],
          "education": [
            {
              "institution": "string (university/college/institute name)",
              "degree": "string (e.g. 'B.Tech', 'B.Sc', 'BCA', 'M.Des', 'Diploma')",
              "field": "string or null (e.g. 'Computer Science', 'Game Design')",
              "start_year": "integer YYYY or null",
              "end_year": "integer YYYY or null"
            }
          ],
          "shipped_titles": [
            {
              "game": "string (published/shipped game or notable game project title)",
              "studio": "string or null (studio/team name, or 'Indie')",
              "role": "string or null (candidate's credited role on that title)",
              "year": "integer YYYY or null (release year)",
              "engine": "string or null (game engine if stated, e.g. 'Unity', 'Unreal Engine', 'Godot')",
              "platforms": ["array of canonical platforms among: 'pc', 'android', 'ios', 'console', 'web'"]
            }
          ]
        }
        """;

    public static string BuildUserPrompt(string redactedResumeText)
    {
        return $"Extract structured JSON from the following PII-redacted resume text:\n\n---\n{redactedResumeText}\n---";
    }
}
