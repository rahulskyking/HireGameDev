namespace HireGameDev.Domain.Entities;

/// <summary>
/// Work history row on a candidate profile (Blueprint v2.0 Section 5, candidate_experiences).
/// </summary>
public sealed class CandidateExperience
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public CandidateProfile? Candidate { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool IsCurrent { get; set; }

    public string? Description { get; set; }
}
