using HireGameDev.Domain.Enums;

namespace HireGameDev.Domain.Entities;

public sealed class JobApplication
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Guid JobId { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Submitted;
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ConsentVersion { get; set; }
    public DateTimeOffset? ConsentedAt { get; set; }
    public DateTimeOffset? SnapshotPurgedAt { get; set; }
    public uint Version { get; set; }
}
