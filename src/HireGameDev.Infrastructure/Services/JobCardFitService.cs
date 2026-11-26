using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Scores a bounded page in one authorized read; never returns the candidate's profile.
internal sealed class JobCardFitService(AppDbContext db, IClock clock) : IJobCardFitService
{
    public async Task<IReadOnlyDictionary<Guid, ApplicationFitScore>> GetAsync(Guid actorUserId, IReadOnlyList<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        var empty = new Dictionary<Guid, ApplicationFitScore>();
        if (actorUserId == Guid.Empty || jobIds.Count == 0 || jobIds.Count > 50 || jobIds.Any(x => x == Guid.Empty)) return empty;
        var ids = jobIds.Distinct().ToArray();
        var now = clock.UtcNow;
        var rows = await (from candidate in db.CandidateProfiles.AsNoTracking()
            join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
            join city in db.Cities.AsNoTracking() on candidate.CityId equals city.Id
            from job in db.Jobs.AsNoTracking()
            join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
            where user.Id == actorUserId && user.IsActive && user.DeletedAt == null && user.AccountType == AccountType.Candidate
                && candidate.DeletedAt == null && ids.Contains(job.Id) && job.Status == JobStatus.Active
                && job.ExpiresAt != null && job.ExpiresAt > now && job.ApplicationsCount < job.ApplicationCap
                && studio.VerificationStatus == StudioVerificationStatus.Verified && studio.SuspendedAt == null && studio.DeletedAt == null
                && !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Id && b.StudioId == studio.Id)
                && !db.Applications.Any(a => a.CandidateId == candidate.Id && a.JobId == job.Id)
            select new {
                job.Id, CandidateId = candidate.Id, candidate.CityId, candidate.YearsExperience, candidate.OpenToRemote,
                job.MinYears, job.WorkMode, JobCityId = job.CityId,
                JobSkills = (from js in db.JobSkills.AsNoTracking() join skill in db.Skills.AsNoTracking() on js.SkillId equals skill.Id
                    where js.JobId == job.Id orderby js.SkillId
                    select new { js.IsRequired, skill.Name, js.MinProficiency }).ToList(),
                CandidateSkills = (from cs in db.CandidateSkills.AsNoTracking() join skill in db.Skills.AsNoTracking() on cs.SkillId equals skill.Id
                    where cs.CandidateId == candidate.Id orderby cs.SkillId
                    select new { skill.Name, cs.Proficiency, cs.YearsUsed, cs.LastUsedYear, cs.IsPrimary }).ToList()
            }).AsSingleQuery().ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var skills = row.CandidateSkills.Select(s => new ApplicationSkillSnapshot(s.Name, s.Proficiency, s.YearsUsed, s.LastUsedYear, s.IsPrimary)).ToArray();
            var candidate = new CandidateApplicationSnapshot("", "", default, "", default, row.YearsExperience, null, default,
                row.OpenToRemote, false, null, null, null, skills, Array.Empty<ApplicationExperienceSnapshot>(),
                Array.Empty<ApplicationEducationSnapshot>(), Array.Empty<ApplicationPortfolioSnapshot>(), Array.Empty<ApplicationGameSnapshot>());
            var required = row.JobSkills.Where(s => s.IsRequired).Select(s => new ApplicationJobSkillSnapshot(s.Name, s.MinProficiency)).ToArray();
            var preferred = row.JobSkills.Where(s => !s.IsRequired).Select(s => new ApplicationJobSkillSnapshot(s.Name, s.MinProficiency)).ToArray();
            var job = new JobApplicationSnapshot("", "", null, null, required,
                new ApplicationMatchInputs(preferred, row.MinYears, row.WorkMode, row.JobCityId.HasValue ? row.CityId == row.JobCityId.Value : null));
            var fit = ApplicationFitScore.Create(candidate, job);
            if (fit is not null) empty[row.Id] = fit;
        }
        return empty;
    }
}
