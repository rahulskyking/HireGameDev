namespace HireGameDev.Domain.Enums;

/// <summary>
/// Origin format of an uploaded resume PDF recorded in <c>resume_parse_logs.source</c> (Blueprint v2.0 Section 4).
/// Stored as <c>'pdf'</c> or <c>'linkedin_pdf'</c> via <c>SnakeCaseEnumValueConverter</c>.
/// </summary>
public enum ResumeParseSource
{
    Pdf = 0,
    LinkedinPdf = 1
}
