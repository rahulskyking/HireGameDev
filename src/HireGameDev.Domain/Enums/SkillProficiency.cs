namespace HireGameDev.Domain.Enums;

/// <summary>
/// Candidate and job skill proficiency tier stored as PostgreSQL smallint (1..4):
/// 1 = Beginner, 2 = Working, 3 = Strong, 4 = Expert.
/// </summary>
public enum SkillProficiency : short
{
    Beginner = 1,
    Working = 2,
    Strong = 3,
    Expert = 4
}
