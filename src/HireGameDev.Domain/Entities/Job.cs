using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Common;

namespace HireGameDev.Domain.Entities;

/// <summary>Nullable content supports incomplete drafts; publication rules live in the lifecycle service.</summary>
public sealed class Job : IAuditableEntity
{
    public Guid Id { get; set; }

    public Guid StudioId { get; set; }

    public Guid PostedByUserId { get; set; }

    public string? Title { get; set; }

    public string Slug { get; set; } = string.Empty;

    public DisciplineCategory? Category { get; set; }

    public JobType? JobType { get; set; }

    public SeniorityLevel? SeniorityMin { get; set; }

    public SeniorityLevel? SeniorityMax { get; set; }

    public decimal? MinYears { get; set; }

    public decimal? MaxYears { get; set; }

    public string? Description { get; set; }

    public JobWorkMode? WorkMode { get; set; }

    public int? CityId { get; set; }

    public bool RelocationAssistance { get; set; }

    public bool FresherFriendly { get; set; }

    public JobPayUnit? PayUnit { get; set; }

    public decimal? SalaryMin { get; set; }

    public decimal? SalaryMax { get; set; }

    public short? MaxNoticeDays { get; set; }

    public short Openings { get; set; } = 1;

    public string? ScreeningQuestion { get; set; }

    public short ApplicationCap { get; set; } = 50;

    public int ApplicationsCount { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Draft;

    public DateTimeOffset? FirstPublishedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public Guid? RemovedByUserId { get; set; }

    public string? RemovalReason { get; set; }

    public string? NormalizedTitle { get; set; }

    public string? ContentFingerprint { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>PostgreSQL xmin concurrency token, never supplied as editable job content.</summary>
    public uint Version { get; set; }
}
