namespace HireGameDev.Domain.Entities;

/// <summary>Versioned submission data; future services must treat the record as immutable.</summary>
public sealed class ApplicationSnapshot
{
    public Guid ApplicationId { get; set; }
    public int SnapshotVersion { get; set; }
    public string CandidateData { get; set; } = "{}";
    public string JobData { get; set; } = "{}";
    public string? Phone { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public DateTimeOffset ConsentedAt { get; set; }
}
