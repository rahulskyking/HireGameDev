namespace HireGameDev.Domain.Enums;

/// <summary>
/// Outcome status of a resume parse attempt recorded in <c>resume_parse_logs.status</c> (Blueprint v2.0 Section 4).
/// Stored as <c>'ok'</c>, <c>'failed'</c>, or <c>'rejected'</c>.
/// </summary>
public enum ResumeParseStatus
{
    Ok = 0,
    Failed = 1,
    Rejected = 2
}
