using System.Data;
using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HireGameDev.Infrastructure.Services;

/// <summary>A consistent private preview, not a consent receipt, slot reservation, or submit operation.</summary>
internal sealed class ApplicationReviewService(AppDbContext db, IApplicationEligibilityService eligibility) : IApplicationReviewService
{
    private const int MaxScreeningAnswerLength = 2000;

    public async Task<Result<ApplicationSnapshotDraft>> ReviewAsync(Guid actorUserId, Guid jobId, string? screeningAnswer, CancellationToken cancellationToken = default)
    {
        if (screeningAnswer is { Length: > MaxScreeningAnswerLength })
            return Result.Failure<ApplicationSnapshotDraft>(Error.Validation("Applications.AnswerTooLong", "Keep the screening answer within 2000 characters."));
        // Split queries must see the same version of the Candidate graph. A preview can still
        // become stale after this transaction; submission must independently coordinate writes,
        // verify the reviewed content and recheck authorization, blocklist, capacity and expiry.
        await using IDbContextTransaction? tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var checkedEligibility = await eligibility.CheckAsync(actorUserId, jobId, cancellationToken);
        if (checkedEligibility.IsFailure) return Result.Failure<ApplicationSnapshotDraft>(checkedEligibility.Error);
        if (checkedEligibility.Value != ApplicationEligibility.Eligible)
            return Result.Failure<ApplicationSnapshotDraft>(Error.Conflict("Applications.NotEligible", "You cannot submit a new application for this job."));

        var candidate = await db.CandidateProfiles.AsNoTracking().AsSplitQuery()
            .Where(c => c.UserId == actorUserId && c.DeletedAt == null)
            .Include(c => c.City)
            .Include(c => c.Skills).ThenInclude(s => s.Skill)
            .Include(c => c.Experiences)
            .Include(c => c.Education)
            .Include(c => c.PortfolioLinks).ThenInclude(p => p.EvidencedSkills).ThenInclude(s => s.Skill)
            .Include(c => c.ShippedTitles).ThenInclude(g => g.EngineSkill)
            .SingleOrDefaultAsync(cancellationToken);
        if (candidate is null || candidate.City is null)
            return Result.Failure<ApplicationSnapshotDraft>(Error.Validation("Applications.ProfileIncomplete", "Complete your Candidate profile before applying."));
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || string.IsNullOrWhiteSpace(job.Title))
            return Result.Failure<ApplicationSnapshotDraft>(Error.NotFound("Applications.Unavailable", "This listing is unavailable."));
        var studio = await db.StudioProfiles.AsNoTracking().SingleOrDefaultAsync(s => s.Id == job.StudioId, cancellationToken);
        if (studio is null)
            return Result.Failure<ApplicationSnapshotDraft>(Error.NotFound("Applications.Unavailable", "This listing is unavailable."));
        if (string.IsNullOrWhiteSpace(job.ScreeningQuestion) && !string.IsNullOrWhiteSpace(screeningAnswer))
            return Result.Failure<ApplicationSnapshotDraft>(Error.Validation("Applications.NoScreeningQuestion", "This job has no screening question."));
        // Do not commit anything: disposal rolls back this read-only transaction.
        return Result.Success(ApplicationSnapshotBuilder.Create(candidate, job, studio, screeningAnswer));
    }
}
