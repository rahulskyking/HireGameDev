namespace HireGameDev.Domain.Enums;

/// <summary>
/// Core skill categories defined by Build Blueprint v2.0 (Section 4, skills.category).
/// Stored as snake_case text in PostgreSQL.
/// </summary>
public enum SkillCategory
{
    Engine = 0,
    Language = 1,
    ArtTool = 2,
    DesignAudioTool = 3,
    Devops = 4,
    Genre = 5,
    Platform = 6,
    Role = 7
}
