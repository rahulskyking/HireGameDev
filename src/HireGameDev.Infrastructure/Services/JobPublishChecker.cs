using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Application.Features.Settings;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

/// <summary>
/// Deterministic, read-only publishing assessment. Not an authorization boundary or reservation:
/// future preview endpoints must authorize first; activation re-runs this inside the lifecycle transaction.
/// </summary>
internal sealed class JobPublishChecker(AppDbContext db, ISettingsService settings, JobDuplicateDetector duplicates) : IJobPublishChecker
{
    public async Task<JobPublishCheckResult> CheckAsync(JobPublishContext context, CancellationToken cancellationToken = default)
    {
        var spread = await settings.GetDecimalAsync(AppSettingKeys.JobSalaryMaxSpread, 1.5m, cancellationToken);
        var findings = JobPublishRules.Evaluate(context.Content, spread).Findings.ToList();
        var studio = await db.StudioProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == context.StudioId && x.DeletedAt == null, cancellationToken);
        if (studio is null || studio.SuspendedAt.HasValue || studio.VerificationStatus != StudioVerificationStatus.Verified)
            findings.Add(new("Jobs.StudioRestricted", JobFindingSeverity.PostingRestriction, null, "A verified, non-suspended Studio is required to publish."));
        if (!await db.Jobs.AsNoTracking().AnyAsync(x => x.Id == context.JobId && x.StudioId == context.StudioId, cancellationToken))
            findings.Add(new("Jobs.NotFound", JobFindingSeverity.PostingRestriction, null, "Save a draft belonging to this Studio before previewing publication."));
        if (context.Content.CityId.HasValue && !await db.Cities.AnyAsync(x => x.Id == context.Content.CityId && x.IsActive, cancellationToken))
            findings.Add(new("Jobs.InactiveCity", JobFindingSeverity.BlockingError, "CityId", "Choose an active city."));
        var skillIds = (context.Content.Skills ?? Array.Empty<JobSkillInput>()).Select(x => x.SkillId).Distinct().ToArray();
        if (await db.Skills.CountAsync(x => skillIds.Contains(x.Id) && x.IsActive, cancellationToken) != skillIds.Length)
            findings.Add(new("Jobs.InactiveSkills", JobFindingSeverity.BlockingError, "Skills", "One or more selected skills are missing or inactive. Choose active skills."));
        // Avoid hashing invalid or unbounded inputs. No writes and no external/AI calls.
        if (!findings.Any(x => x.Severity != JobFindingSeverity.Warning))
        {
            var duplicate = await duplicates.CheckAsync(context, cancellationToken);
            if (duplicate is not null) findings.Add(duplicate);
        }
        return new(findings);
    }
}
