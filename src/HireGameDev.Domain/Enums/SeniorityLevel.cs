namespace HireGameDev.Domain.Enums;

/// <summary>
/// Ordered seniority tiers stored as snake_case text in PostgreSQL:
/// 'fresher', 'junior', 'mid', 'senior', 'lead'.
/// Integer ordering (0..4) enforces the +/-1 level self-selection rule and job seniority bands.
/// </summary>
public enum SeniorityLevel
{
    Fresher = 0,
    Junior = 1,
    Mid = 2,
    Senior = 3,
    Lead = 4
}
