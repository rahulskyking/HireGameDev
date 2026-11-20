namespace HireGameDev.Domain.Entities;

/// <summary>
/// Employer-privacy blocklist entry (Blueprint v2.0 Section 5 / Page 33, ADR-020).
/// Hides a candidate profile from studio discovery/search and unsolicited profile/avatar views
/// by members of the specified studio. M2.3c also excludes that Studio’s jobs from the
/// signed-in Candidate’s search/detail views (a preference, not anonymous access control).
/// Existing explicit-application exception remains for Phase 3 implementation.
/// </summary>
public sealed class CandidateStudioBlock
{
    public Guid CandidateId { get; set; }

    public CandidateProfile? Candidate { get; set; }

    public Guid StudioId { get; set; }

    public StudioProfile? Studio { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
