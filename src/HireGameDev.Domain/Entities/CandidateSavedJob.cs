namespace HireGameDev.Domain.Entities;

/// <summary>A private Candidate preference; visibility is always evaluated when reading.</summary>
public sealed class CandidateSavedJob
{
    public Guid CandidateId { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
