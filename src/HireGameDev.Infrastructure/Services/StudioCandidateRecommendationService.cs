using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Development-only unsolicited discovery. No contact details, private compensation,
// portfolio contents, application snapshots, activity or Premium state are returned.
internal sealed class StudioCandidateRecommendationService(AppDbContext db, IClock clock) : IStudioCandidateRecommendationService
{
    public async Task<Result<StudioCandidateRecommendationsPage>> GetAsync(Guid actorUserId, Guid studioId, Guid jobId, CancellationToken cancellationToken = default)
    {
        static Error Unavailable() => Error.NotFound("Recommendations.Unavailable", "This job is unavailable.");
        if (actorUserId == Guid.Empty || studioId == Guid.Empty || jobId == Guid.Empty) return Result.Failure<StudioCandidateRecommendationsPage>(Unavailable());
        var now = clock.UtcNow.ToUniversalTime();
        // This query also permits a useful empty state without exposing another Studio's job.
        var jobInfo = await (from job in db.Jobs.AsNoTracking()
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            join member in db.StudioMembers.AsNoTracking() on studio.Id equals member.StudioId
            join actor in db.Users.AsNoTracking() on member.UserId equals actor.Id
            where job.Id == jobId && studio.Id == studioId && actor.Id == actorUserId && actor.AccountType == AccountType.Studio && actor.IsActive && actor.DeletedAt == null &&
                (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer) &&
                studio.VerificationStatus == StudioVerificationStatus.Verified && studio.SuspendedAt == null && studio.DeletedAt == null &&
                job.Status == JobStatus.Active && job.ExpiresAt != null && job.ExpiresAt > now && job.ApplicationsCount < job.ApplicationCap
            select new { job.Id, job.Title }).SingleOrDefaultAsync(cancellationToken);
        if (jobInfo is null) return Result.Failure<StudioCandidateRecommendationsPage>(Unavailable());
        // Authorization, blocks and discoverability are rechecked in the same statement as
        // profile data; the job's current requirements and profile skills come from that read.
        var rows = await (from job in db.Jobs.AsNoTracking()
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            join member in db.StudioMembers.AsNoTracking() on studio.Id equals member.StudioId
            join actor in db.Users.AsNoTracking() on member.UserId equals actor.Id
            from candidate in db.CandidateProfiles.AsNoTracking()
            join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
            where job.Id == jobId && studio.Id == studioId && actor.Id == actorUserId && actor.IsActive && actor.DeletedAt == null && actor.AccountType == AccountType.Studio &&
                (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer) &&
                studio.VerificationStatus == StudioVerificationStatus.Verified && studio.SuspendedAt == null && studio.DeletedAt == null &&
                job.Status == JobStatus.Active && job.ExpiresAt != null && job.ExpiresAt > now && job.ApplicationsCount < job.ApplicationCap &&
                candidate.IsDiscoverable && candidate.Completeness >= CandidateProfileRules.DiscoverableCompletenessThreshold && candidate.DeletedAt == null &&
                candidate.Availability != CandidateAvailability.NotLooking && user.IsActive && user.DeletedAt == null && user.AccountType == AccountType.Candidate &&
                !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Id && b.StudioId == studio.Id) &&
                !db.Applications.Any(a => a.CandidateId == candidate.Id && a.JobId == job.Id)
            let matched = (from js in db.JobSkills
                join cs in db.CandidateSkills on js.SkillId equals cs.SkillId
                where js.JobId == job.Id && cs.CandidateId == candidate.Id && (int)cs.Proficiency >= (int)js.MinProficiency
                select cs.SkillId).Count()
            where matched > 0
            orderby matched descending, candidate.Id
            select new { CandidateId = candidate.Id, candidate.FullName, candidate.Headline, candidate.YearsExperience, candidate.OpenToRemote,
                CandidateCityId = candidate.CityId, JobCityId = job.CityId, job.MinYears, job.WorkMode,
                CandidateSkills = (from cs in db.CandidateSkills.AsNoTracking() join skill in db.Skills.AsNoTracking() on cs.SkillId equals skill.Id
                    where cs.CandidateId == candidate.Id orderby cs.SkillId
                    select new { skill.Name, cs.Proficiency, cs.YearsUsed, cs.LastUsedYear, cs.IsPrimary }).ToList(),
                JobSkills = (from js in db.JobSkills.AsNoTracking() join skill in db.Skills.AsNoTracking() on js.SkillId equals skill.Id
                    where js.JobId == job.Id orderby js.SkillId
                    select new { skill.Name, js.IsRequired, js.MinProficiency }).ToList()
            }).Take(50).AsSingleQuery().ToListAsync(cancellationToken);
        var scored = rows.Select(row => {
            var skills = row.CandidateSkills.Select(s => new ApplicationSkillSnapshot(s.Name, s.Proficiency, s.YearsUsed, s.LastUsedYear, s.IsPrimary)).ToArray();
            var candidate = new CandidateApplicationSnapshot("", "", default, "", default, row.YearsExperience, null, default,
                row.OpenToRemote, false, null, null, null, skills, Array.Empty<ApplicationExperienceSnapshot>(),
                Array.Empty<ApplicationEducationSnapshot>(), Array.Empty<ApplicationPortfolioSnapshot>(), Array.Empty<ApplicationGameSnapshot>());
            var required = row.JobSkills.Where(s => s.IsRequired).Select(s => new ApplicationJobSkillSnapshot(s.Name, s.MinProficiency)).ToArray();
            var preferred = row.JobSkills.Where(s => !s.IsRequired).Select(s => new ApplicationJobSkillSnapshot(s.Name, s.MinProficiency)).ToArray();
            var job = new JobApplicationSnapshot("", "", null, null, required,
                new ApplicationMatchInputs(preferred, row.MinYears, row.WorkMode, row.JobCityId.HasValue ? row.CandidateCityId == row.JobCityId.Value : null));
            return new { row.CandidateId, row.FullName, row.Headline, Fit = ApplicationFitScore.Create(candidate, job) };
        }).Where(x => x.Fit?.Percentage is >= 60 && x.Fit.Coverage >= 70 && x.Fit.Criteria.Any(c =>
            (c.Label.StartsWith("Required: ", StringComparison.Ordinal) || c.Label.StartsWith("Preferred: ", StringComparison.Ordinal)) && c.Fraction == 1m))
          .OrderByDescending(x => x.Fit!.Percentage).ThenByDescending(x => x.Fit!.Coverage).ThenBy(x => x.CandidateId)
          .Take(6).Select(x => new StudioCandidateRecommendation(x.FullName, x.Headline, x.Fit!)).ToArray();
        return Result.Success(new StudioCandidateRecommendationsPage(jobId, studioId, jobInfo.Title ?? "Job", scored));
    }
}
