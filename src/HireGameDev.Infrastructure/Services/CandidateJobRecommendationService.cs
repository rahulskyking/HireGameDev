using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Development-only entry point. Bounded retrieval, V1 scoring, no payment/activity input.
internal sealed class CandidateJobRecommendationService(AppDbContext db, IClock clock, IJobCardFitService fits, IPublicJobQueryService jobs, IRecommendationPolicyService policies) : ICandidateJobRecommendationService
{
    public async Task<Result<CandidateJobRecommendationsPage>> GetAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var candidateId = await (from c in db.CandidateProfiles.AsNoTracking()
            join u in db.Users.AsNoTracking() on c.UserId equals u.Id
            where actorUserId != Guid.Empty && u.Id == actorUserId && u.IsActive && u.DeletedAt == null &&
                u.AccountType == AccountType.Candidate && c.DeletedAt == null
            select (Guid?)c.Id).SingleOrDefaultAsync(cancellationToken);
        if (!candidateId.HasValue)
            return Result.Failure<CandidateJobRecommendationsPage>(Error.Forbidden("Applications.CandidateRequired", "An active Candidate account is required."));
        var policyResult = await policies.GetAsync(cancellationToken);
        if (policyResult.IsFailure) return Result.Failure<CandidateJobRecommendationsPage>(policyResult.Error);
        var policy = policyResult.Value;
        var now = clock.UtcNow.ToUniversalTime();
        // Relevance-first retrieval using the existing candidate/job skill indexes. This is a
        // bounded candidate pool, not an exhaustive or calibrated global ranking.
        var pool = await (from j in db.Jobs.AsNoTracking()
            join studio in db.StudioProfiles.AsNoTracking() on j.StudioId equals studio.Id
            where j.Status == JobStatus.Active && j.ExpiresAt != null && j.ExpiresAt > now && j.ApplicationsCount < j.ApplicationCap &&
                studio.VerificationStatus == StudioVerificationStatus.Verified && studio.SuspendedAt == null && studio.DeletedAt == null &&
                !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidateId && b.StudioId == studio.Id) &&
                !db.Applications.Any(a => a.CandidateId == candidateId && a.JobId == j.Id)
            let matched = (from js in db.JobSkills
                join cs in db.CandidateSkills on js.SkillId equals cs.SkillId
                where js.JobId == j.Id && cs.CandidateId == candidateId && (int)cs.Proficiency >= (int)js.MinProficiency
                select js.SkillId).Count()
            where matched > 0
            orderby matched descending, j.PublishedAt descending, j.Id
            select new { j.Id, j.Slug }).Take(50).ToArrayAsync(cancellationToken);
        var scores = await fits.GetAsync(actorUserId, pool.Select(x => x.Id).ToArray(), cancellationToken);
        var candidates = pool.Where(x => scores.TryGetValue(x.Id, out var score) && policy.Allows(score))
            .OrderByDescending(x => scores[x.Id].Percentage).ThenByDescending(x => scores[x.Id].Coverage).ThenBy(x => x.Id).Take(12).ToArray();
        var visible = new List<(Guid Id, HireGameDev.Application.Features.Jobs.PublicJobCard Job)>();
        foreach (var row in candidates)
        {
            var detail = await jobs.GetBySlugAsync(row.Slug, cancellationToken, actorUserId);
            if (detail.IsSuccess && detail.Value.Summary.Id == row.Id) visible.Add((row.Id, detail.Value.Summary));
        }
        // Recheck eligibility and score after catalog reads; changes can still happen after a
        // response has begun, so application submission must independently revalidate.
        var fresh = await fits.GetAsync(actorUserId, visible.Select(x => x.Id).ToArray(), cancellationToken);
        IReadOnlyList<CandidateJobRecommendation> result = visible
            .Where(x => fresh.TryGetValue(x.Id, out var score) && policy.Allows(score))
            .Select(x => new CandidateJobRecommendation(x.Job, fresh[x.Id]))
            .OrderByDescending(x => x.Fit.Percentage).ThenByDescending(x => x.Fit.Coverage).ThenBy(x => x.Job.Id)
            .Take(6).ToArray();
        return Result.Success(new CandidateJobRecommendationsPage(result, policy));
    }

}
