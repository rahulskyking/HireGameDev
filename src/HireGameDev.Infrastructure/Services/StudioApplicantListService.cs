using System.Text.Json;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Prototype only. A single SQL statement gives authorization and data the same database
// snapshot. It does not revoke a response already in flight when access changes afterwards.
internal sealed class StudioApplicantListService(AppDbContext db) : IStudioApplicantListService
{
    public async Task<Result<StudioApplicantPage>> ListAsync(Guid actorUserId, Guid jobId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default, ApplicationStatus? status = null)
    {
        if (!db.Database.IsNpgsql())
            return Result.Failure<StudioApplicantPage>(Error.Failure("Applications.PostgresRequired", "This operation requires PostgreSQL."));
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            return Result.Failure<StudioApplicantPage>(Error.Conflict("Applications.UnitOfWorkBusy", "This operation requires a clean unit of work."));
        // Bound offset and page size before arithmetic or querying.
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue - 100)
            return Result.Failure<StudioApplicantPage>(Error.Validation("Applications.InvalidPage", "Choose a valid page and page size."));
        if (status.HasValue && !Enum.IsDefined(status.Value))
            return Result.Failure<StudioApplicantPage>(Error.Validation("Applications.InvalidStatus", "Choose a valid status."));
        if (actorUserId == Guid.Empty || jobId == Guid.Empty)
            return Result.Success(new StudioApplicantPage(Array.Empty<StudioApplicantListItem>(), false));

        // No live Candidate data is returned. Snapshot existence and purged/withdrawn state
        // are checked in the same query as membership, account state and blocks.
        var items = await (from app in db.Applications.AsNoTracking()
            join job in db.Jobs.AsNoTracking() on app.JobId equals job.Id
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            join member in db.StudioMembers.AsNoTracking() on studio.Id equals member.StudioId
            join actor in db.Users.AsNoTracking() on member.UserId equals actor.Id
            join candidate in db.CandidateProfiles.AsNoTracking() on app.CandidateId equals candidate.Id
            join candidateUser in db.Users.AsNoTracking() on candidate.UserId equals candidateUser.Id
            join snapshot in db.ApplicationSnapshots.AsNoTracking() on app.Id equals snapshot.ApplicationId
            where job.Id == jobId && actor.Id == actorUserId && actor.IsActive && actor.DeletedAt == null && actor.AccountType == AccountType.Studio
                && studio.DeletedAt == null && studio.SuspendedAt == null && studio.VerificationStatus == StudioVerificationStatus.Verified
                && (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer)
                && candidate.DeletedAt == null && candidateUser.IsActive && candidateUser.DeletedAt == null && candidateUser.AccountType == AccountType.Candidate
                && app.Status != ApplicationStatus.Withdrawn && app.SnapshotPurgedAt == null
                && (!status.HasValue || app.Status == status)
                && !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Id && b.StudioId == studio.Id)
            orderby app.SubmittedAt descending, app.Id descending
            select new { app.Id, app.Status, app.SubmittedAt, snapshot.SnapshotVersion, snapshot.CandidateData, snapshot.JobData,
                job.MinYears, job.WorkMode,
                CurrentSkills = (from js in db.JobSkills.AsNoTracking()
                    join skill in db.Skills.AsNoTracking() on js.SkillId equals skill.Id
                    where js.JobId == job.Id orderby js.SkillId
                    select new { js.IsRequired, skill.Name, js.MinProficiency }).ToList() })
            .Skip((page - 1) * pageSize).Take(pageSize + 1).AsSingleQuery().ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        var result = items.Select(x => {
            var required = x.CurrentSkills.Where(s => s.IsRequired).Select(s => new ApplicationJobSkillSnapshot(s.Name, s.MinProficiency)).ToArray();
            var preferred = x.CurrentSkills.Where(s => !s.IsRequired).Select(s => new ApplicationJobSkillSnapshot(s.Name, s.MinProficiency)).ToArray();
            var (fit, currentJob) = SnapshotFit(x.SnapshotVersion, x.CandidateData, x.JobData, required, preferred, x.MinYears, x.WorkMode);
            return new StudioApplicantListItem(x.Id, x.Status, x.SubmittedAt,
                SnapshotName(x.SnapshotVersion, x.CandidateData), fit, currentJob);
        }).ToArray();
        return Result.Success(new StudioApplicantPage(result, hasMore));
    }

    private static (ApplicationFitScore? Fit, bool CurrentJob) SnapshotFit(int version, string candidateJson, string jobJson,
        ApplicationJobSkillSnapshot[] required, ApplicationJobSkillSnapshot[] preferred, decimal? minYears, JobWorkMode? workMode)
    {
        if (version != 1) return (null, false);
        try
        {
            var candidate = JsonSerializer.Deserialize<CandidateApplicationSnapshot>(candidateJson);
            var job = JsonSerializer.Deserialize<JobApplicationSnapshot>(jobJson);
            // Match the detail view's fail-closed handling of incomplete snapshots.
            if (candidate is null || job is null || string.IsNullOrWhiteSpace(candidate.FullName) ||
                string.IsNullOrWhiteSpace(candidate.Headline) || string.IsNullOrWhiteSpace(candidate.City) ||
                candidate.Skills is null || candidate.Experience is null || candidate.Education is null ||
                candidate.Portfolio is null || candidate.ShippedGames is null ||
                candidate.Skills.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name)) ||
                candidate.Experience.Any(x => x is null || string.IsNullOrWhiteSpace(x.Company) || string.IsNullOrWhiteSpace(x.Title)) ||
                candidate.Education.Any(x => x is null || string.IsNullOrWhiteSpace(x.Institution) || string.IsNullOrWhiteSpace(x.Degree)) ||
                candidate.Portfolio.Any(x => x is null || x.Skills is null || x.Skills.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(x.Url)) ||
                candidate.ShippedGames.Any(x => x is null || x.Platforms is null || x.Platforms.Any(string.IsNullOrWhiteSpace) ||
                    string.IsNullOrWhiteSpace(x.GameName) || string.IsNullOrWhiteSpace(x.StudioName) || string.IsNullOrWhiteSpace(x.Role)) ||
                string.IsNullOrWhiteSpace(job.JobTitle) || string.IsNullOrWhiteSpace(job.StudioName)) return (null, false);
            var legacy = job.MatchInputs is null;
            if (legacy) job = job with { RequiredSkills = required, MatchInputs = new ApplicationMatchInputs(preferred, minYears, workMode, null) };
            return (ApplicationFitScore.Create(candidate, job), legacy);
        }
        catch (JsonException) { return (null, false); }
        catch (NotSupportedException) { return (null, false); }
    }

    // Only expose the versioned snapshot's name, never an unknown JSON field or live profile.
    // Invalid snapshots remain generic rather than falling back to a different source.
    private static string? SnapshotName(int version, string json)
    {
        if (version != 1) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("FullName", out var name) || name.ValueKind != JsonValueKind.String) return null;
            var value = name.GetString();
            return string.IsNullOrWhiteSpace(value) || value.Length > 200 ? null : value;
        }
        catch (JsonException) { return null; }
    }
}
