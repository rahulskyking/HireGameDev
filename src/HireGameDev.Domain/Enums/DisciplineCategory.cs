namespace HireGameDev.Domain.Enums;

/// <summary>
/// Primary game-development discipline stored as snake_case text in PostgreSQL:
/// 'programming', 'art', 'design', 'audio', 'qa', 'production'.
/// </summary>
public enum DisciplineCategory
{
    Programming = 0,
    Art = 1,
    Design = 2,
    Audio = 3,
    Qa = 4,
    Production = 5
}
