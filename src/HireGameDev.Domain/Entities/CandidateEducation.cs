namespace HireGameDev.Domain.Entities;

/// <summary>
/// Education row on a candidate profile (Blueprint v2.0 Section 5, candidate_education).
/// </summary>
public sealed class CandidateEducation
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public CandidateProfile? Candidate { get; set; }

    public string Institution { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;

    public string? Field { get; set; }

    public short? StartYear { get; set; }

    public short? EndYear { get; set; }
}
