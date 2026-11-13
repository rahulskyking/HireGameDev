using HireGameDev.Application.Features.Jobs;
using HireGameDev.Application.Features.ReferenceData;
using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Web.Models.Jobs;

public sealed class JobEditorModel
{
    public Guid StudioId { get; set; }
    public Guid? JobId { get; set; }
    public uint? Version { get; set; }
    public JobContent Content { get; set; } = new();
    public List<JobSkillRow> Skills { get; set; } = new();
    public IReadOnlyList<CityLookupItem> Cities { get; set; } = Array.Empty<CityLookupItem>();
    public IReadOnlyList<SkillLookupItem> AvailableSkills { get; set; } = Array.Empty<SkillLookupItem>();
}
public sealed class JobSkillRow
{
    public int? SkillId { get; set; }
    public bool IsRequired { get; set; }
    public SkillProficiency MinProficiency { get; set; } = SkillProficiency.Working;
}
public sealed record JobListPage(StudioMembershipSummary Studio, IReadOnlyList<StudioMembershipSummary> Memberships,
    StudioJobList Jobs, JobStatus? Filter);
public sealed record JobManagePage(JobSnapshot Job, bool CanWrite,
    IReadOnlyList<JobPublishFinding>? Findings = null, string? Error = null);
