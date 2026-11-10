using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

/// <summary>
/// Privacy-preserving operational log for Resume AI Import attempts (<c>resume_parse_logs</c>).
/// Intentionally stores ONLY char counts, latency, source, and status — NEVER raw or redacted resume text.
/// </summary>
public sealed class ResumeParseLog
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public ResumeParseSource Source { get; set; } = ResumeParseSource.Pdf;

    public ResumeParseStatus Status { get; set; }

    public int? InputChars { get; set; }

    public int? DurationMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
