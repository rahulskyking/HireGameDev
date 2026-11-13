using HireGameDev.Domain.Enums;

namespace HireGameDev.Application.Features.Jobs;

// These are backend commands: ActorUserId must come from authenticated server context, never form binding.
public sealed record JobSkillInput(int SkillId, bool IsRequired, SkillProficiency MinProficiency);

public sealed record JobContent
{
    public string? Title { get; init; }
    public DisciplineCategory? Category { get; init; }
    public JobType? JobType { get; init; }
    public SeniorityLevel? SeniorityMin { get; init; }
    public SeniorityLevel? SeniorityMax { get; init; }
    public decimal? MinYears { get; init; }
    public decimal? MaxYears { get; init; }
    public string? Description { get; init; }
    public JobWorkMode? WorkMode { get; init; }
    public int? CityId { get; init; }
    public bool RelocationAssistance { get; init; }
    public bool FresherFriendly { get; init; }
    public JobPayUnit? PayUnit { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public short? MaxNoticeDays { get; init; }
    public short Openings { get; init; } = 1;
    public string? ScreeningQuestion { get; init; }
    public IReadOnlyList<JobSkillInput> Skills { get; init; } = Array.Empty<JobSkillInput>();
}

public enum JobLifecycleAction { Publish, Pause, Resume, ReopenFull, Renew, Close }
public enum StudioJobRestrictionAction { Suspend, LiftSuspension }
public enum JobFindingSeverity { BlockingError, PostingRestriction, Warning }
public sealed record JobPublishFinding(string Code, JobFindingSeverity Severity, string? Field, string Message);
public sealed record JobPublishCheckResult(IReadOnlyList<JobPublishFinding> Findings)
{
    public bool CanPublish => !Findings.Any(x => x.Severity != JobFindingSeverity.Warning);
}
public sealed record JobPublishContext(Guid JobId, Guid StudioId, JobContent Content);

public sealed record JobSnapshot(
    Guid Id, Guid StudioId, string Slug, JobStatus Status, JobContent Content,
    short ApplicationCap, int ApplicationsCount, DateTimeOffset? FirstPublishedAt,
    DateTimeOffset? PublishedAt, DateTimeOffset? ExpiresAt, uint Version);

public sealed record SaveJobCommand(Guid ActorUserId, Guid StudioId, JobContent Content,
    Guid? JobId = null, uint? ExpectedVersion = null);
public sealed record JobTransitionCommand(Guid ActorUserId, Guid StudioId, Guid JobId,
    JobLifecycleAction Action, uint? ExpectedVersion = null);
// A successful Result means the request was evaluated. Callers must inspect Applied before reporting a transition.
public sealed record JobLifecycleOutcome(bool Applied, JobSnapshot Job, IReadOnlyList<JobPublishFinding> Findings);
public sealed record RemoveJobCommand(Guid ActorUserId, Guid JobId, string Reason);
public sealed record StudioJobRestrictionCommand(Guid ActorUserId, Guid StudioId,
    StudioJobRestrictionAction Action, string Reason);

public sealed class JobLifecycleOptions
{
    public const string SectionName = "Jobs";
    public int ValidityDays { get; set; } = 30;
    public int FreeActiveJobs { get; set; } = 3;
    public int PublicationsPer24Hours { get; set; } = 5;
    public int RepeatCooldownDays { get; set; } = 7;
    public int MaxDraftsPerStudio { get; set; } = 20;
}

public sealed record StudioJobListItem(Guid Id, string? Title, JobStatus Status, DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt);
public sealed record StudioJobList(IReadOnlyList<StudioJobListItem> Items,
    IReadOnlyDictionary<JobStatus, int> Counts, int Page, int TotalPages);
