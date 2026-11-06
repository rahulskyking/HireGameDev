namespace HireGameDev.Domain.Enums;

/// <summary>
/// Evidence tier backing a candidate skill stored as PostgreSQL smallint (0..2):
/// 0 = Claimed (0.70x), 1 = Backed by portfolio/title/experience (0.90x), 2 = Verified credit/challenge (1.00x).
/// </summary>
public enum SkillEvidenceLevel : short
{
    Claimed = 0,
    Backed = 1,
    Verified = 2
}
