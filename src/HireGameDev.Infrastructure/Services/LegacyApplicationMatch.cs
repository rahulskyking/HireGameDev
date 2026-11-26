using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// For older applications only: compare the submitted candidate snapshot with CURRENT job
// requirements. Recheck access in the same statement that reads those requirements.
internal static class LegacyApplicationMatch
{
    public static async Task<ApplicationFitScore?> GetAsync(AppDbContext db, Guid actorUserId, Guid applicationId,
        CandidateApplicationSnapshot candidateSnapshot, JobApplicationSnapshot jobSnapshot, CancellationToken cancellationToken)
    {
        var row = await (from app in db.Applications.AsNoTracking()
            join job in db.Jobs.AsNoTracking() on app.JobId equals job.Id
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            join candidate in db.CandidateProfiles.AsNoTracking() on app.CandidateId equals candidate.Id
            join candidateUser in db.Users.AsNoTracking() on candidate.UserId equals candidateUser.Id
            join actor in db.Users.AsNoTracking() on actorUserId equals actor.Id
            join snapshot in db.ApplicationSnapshots.AsNoTracking() on app.Id equals snapshot.ApplicationId
            where app.Id == applicationId && app.Status != ApplicationStatus.Withdrawn && app.SnapshotPurgedAt == null && snapshot.SnapshotVersion == 1
                && actor.IsActive && actor.DeletedAt == null && candidate.DeletedAt == null && candidateUser.IsActive && candidateUser.DeletedAt == null
                && candidateUser.AccountType == AccountType.Candidate && studio.DeletedAt == null && studio.SuspendedAt == null
                && studio.VerificationStatus == StudioVerificationStatus.Verified
                && !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Id && b.StudioId == studio.Id)
                && ((actor.AccountType == AccountType.Candidate && actor.Id == candidateUser.Id && job.Status != JobStatus.Removed)
                    || (actor.AccountType == AccountType.Studio && db.StudioMembers.Any(m => m.StudioId == studio.Id && m.UserId == actor.Id &&
                        (m.MemberRole == StudioMemberRole.Owner || m.MemberRole == StudioMemberRole.Recruiter || m.MemberRole == StudioMemberRole.Viewer))))
            select new {
                job.MinYears, job.WorkMode,
                Skills = (from js in db.JobSkills.AsNoTracking()
                          join skill in db.Skills.AsNoTracking() on js.SkillId equals skill.Id
                          where js.JobId == job.Id
                          orderby js.SkillId
                          select new { js.IsRequired, skill.Name, js.MinProficiency }).ToList()
            }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var required = row.Skills.Where(x => x.IsRequired).Select(x => new ApplicationJobSkillSnapshot(x.Name, x.MinProficiency)).ToArray();
        var preferred = row.Skills.Where(x => !x.IsRequired).Select(x => new ApplicationJobSkillSnapshot(x.Name, x.MinProficiency)).ToArray();
        // Historical city identity was not captured; do not infer it from a city name.
        var currentJob = jobSnapshot with { RequiredSkills = required, MatchInputs = new ApplicationMatchInputs(preferred, row.MinYears, row.WorkMode, null) };
        return ApplicationFitScore.Create(candidateSnapshot, currentJob);
    }
}
