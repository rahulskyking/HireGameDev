using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

internal sealed class PublicJobQueryService(AppDbContext db, IClock clock, IFileStorageService storage) : IPublicJobQueryService
{
    public async Task<Result<PublicJobSearchPage>> SearchAsync(PublicJobSearch search, CancellationToken cancellationToken = default, Guid? viewerUserId = null)
    {
        if (search.Keyword?.Length > 100 || search.Keyword?.Any(char.IsControl) == true ||
            search.SkillIds is null || search.SkillIds.Count > 12 || search.SkillIds.Any(x => x <= 0) ||
            search.StudioId == Guid.Empty || search.CityId <= 0 || search.ExperienceYears is < 0 or > 99.9m || search.PageSize is < 1 or > 50 ||
            (search.Category.HasValue && !Enum.IsDefined(search.Category.Value)) ||
            (search.JobType.HasValue && !Enum.IsDefined(search.JobType.Value)) ||
            (search.WorkMode.HasValue && !Enum.IsDefined(search.WorkMode.Value)) || !Enum.IsDefined(search.Sort))
            return Result.Failure<PublicJobSearchPage>(Error.Validation("PublicJobs.InvalidSearch", "Choose valid filters, up to 12 skills, a keyword within 100 characters and a page size from 1 to 50."));
        var terms = (search.Keyword ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.ToLowerInvariant()).Distinct().ToArray();
        if (terms.Length > 8)
            return Result.Failure<PublicJobSearchPage>(Error.Validation("PublicJobs.InvalidSearch", "Use at most eight keyword terms."));
        var query = await VisibleForViewerAsync(viewerUserId, cancellationToken);
        // AND between terms, OR between title/description. Contains is parameterized literal
        // substring matching, not caller-provided SQL LIKE syntax (%, _ are literal characters).
        foreach (var term in terms)
            query = query.Where(x => (x.Job.Title != null && x.Job.Title.ToLower().Contains(term)) ||
                (x.Job.Description != null && x.Job.Description.ToLower().Contains(term)));
        if (search.StudioId.HasValue) query = query.Where(x => x.Studio.Id == search.StudioId.Value);
        if (search.Category.HasValue) query = query.Where(x => x.Job.Category == search.Category);
        if (search.JobType.HasValue) query = query.Where(x => x.Job.JobType == search.JobType);
        if (search.WorkMode.HasValue) query = query.Where(x => x.Job.WorkMode == search.WorkMode);
        if (search.CityId.HasValue) query = query.Where(x => x.Job.CityId == search.CityId);
        if (search.ExperienceYears.HasValue)
            query = query.Where(x => x.Job.MinYears <= search.ExperienceYears &&
                (!x.Job.MaxYears.HasValue || x.Job.MaxYears >= search.ExperienceYears));
        if (search.FresherFriendlyOnly) query = query.Where(x => x.Job.FresherFriendly);
        foreach (var skill in search.SkillIds.Distinct())
            query = query.Where(x => db.JobSkills.Any(s => s.JobId == x.Job.Id && s.SkillId == skill));
        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)search.PageSize));
        var page = Math.Clamp(search.Page, 1, pages);
        // Normalize the minimum advertised pay to INR lakhs per year for comparison.
        // This is a sort of advertised lower bounds, not a guarantee of compensation.
        var ordered = search.Sort switch
        {
            PublicJobSort.ClosingSoon => query.OrderBy(x => x.Job.ExpiresAt).ThenBy(x => x.Job.Id),
            PublicJobSort.HighestStartingPay => query.OrderByDescending(x => x.Job.PayUnit == JobPayUnit.MonthlyInr
                ? (x.Job.SalaryMin ?? 0m) * 12m / 100000m : (x.Job.SalaryMin ?? 0m)).ThenBy(x => x.Job.Id),
            _ => query.OrderByDescending(x => x.Job.PublishedAt).ThenBy(x => x.Job.Id)
        };
        var rows = await ordered.Skip((page - 1) * search.PageSize).Take(search.PageSize).ToArrayAsync(cancellationToken);
        var cityIds = rows.Where(x => x.Job.CityId.HasValue).Select(x => x.Job.CityId!.Value).Distinct().ToArray();
        var cities = await db.Cities.AsNoTracking().Where(x => cityIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var cards = rows.Select(x => Card(x, x.Job.CityId.HasValue ? cities.GetValueOrDefault(x.Job.CityId.Value) : null)).ToArray();
        return Result.Success(new PublicJobSearchPage(cards, total, page, search.PageSize, pages));
    }

    public async Task<Result<PublicJobDetail>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default, Guid? viewerUserId = null)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 160) return Result.Failure<PublicJobDetail>(Unavailable());
        slug = slug.Trim().ToLowerInvariant();
        if (slug.Length < 2 || slug.StartsWith('-') || slug.EndsWith('-') || slug.Contains("--") ||
            slug.Any(c => c is not (>= 'a' and <= 'z') && c is not (>= '0' and <= '9') && c != '-'))
            return Result.Failure<PublicJobDetail>(Unavailable());
        var query = await VisibleForViewerAsync(viewerUserId, cancellationToken);
        var row = await query.SingleOrDefaultAsync(x => x.Job.Slug == slug, cancellationToken);
        if (row is null) return Result.Failure<PublicJobDetail>(Unavailable());
        var city = row.Job.CityId.HasValue ? await db.Cities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.Job.CityId, cancellationToken) : null;
        var skills = await (from js in db.JobSkills.AsNoTracking()
                            join s in db.Skills.AsNoTracking() on js.SkillId equals s.Id
                            where js.JobId == row.Job.Id
                            orderby js.IsRequired descending, s.Name, s.Id
                            select new PublicJobSkill(s.Id, s.Name, js.IsRequired, js.MinProficiency)).ToArrayAsync(cancellationToken);
        var j = row.Job;
        return Result.Success(new PublicJobDetail(Card(row, city), j.Category!.Value, j.SeniorityMin!.Value,
            j.SeniorityMax!.Value, j.Description!, j.RelocationAssistance, j.Openings, j.MaxNoticeDays, j.ScreeningQuestion, skills));
    }

    private async Task<IQueryable<Listing>> VisibleForViewerAsync(Guid? viewerUserId, CancellationToken ct)
    {
        var query = Visible(clock.UtcNow.ToUniversalTime());
        if (!viewerUserId.HasValue || viewerUserId == Guid.Empty) return query;
        // Database identity is authoritative: ignore legacy opposite profiles/roles, inactive
        // accounts and deleted profiles. CandidateProfile.Id is NOT the Identity User.Id.
        var candidateId = await (from candidate in db.CandidateProfiles.AsNoTracking()
                                 join user in db.Users.AsNoTracking() on candidate.UserId equals user.Id
                                 where user.Id == viewerUserId.Value && user.IsActive && user.DeletedAt == null &&
                                     user.AccountType == AccountType.Candidate && candidate.DeletedAt == null
                                 select (Guid?)candidate.Id).SingleOrDefaultAsync(ct);
        if (candidateId.HasValue)
            query = query.Where(x => !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidateId.Value && b.StudioId == x.Studio.Id));
        return query;
    }

    // SQL-translatable equivalent of JobLifecycleRules.CanAppearPublicly. One gate shared by
    // list and detail; scheduler execution is never a visibility/security prerequisite.
    private IQueryable<Listing> Visible(DateTimeOffset now) =>
        from job in db.Jobs.AsNoTracking()
        join studio in db.StudioProfiles.AsNoTracking() on job.StudioId equals studio.Id
        where job.Status == JobStatus.Active && job.ExpiresAt.HasValue && job.ExpiresAt > now &&
              studio.VerificationStatus == StudioVerificationStatus.Verified && studio.SuspendedAt == null && studio.DeletedAt == null
        select new Listing { Job = job, Studio = studio };

    private PublicJobCard Card(Listing row, City? city)
    {
        var j = row.Job;
        // Explicit allowlist: never return entities, creator IDs, version, fingerprints,
        // internal notes, application counts or moderation metadata to public callers.
        var studio = new PublicJobStudio(row.Studio.Id, row.Studio.Name, row.Studio.Slug,
            string.IsNullOrWhiteSpace(row.Studio.LogoPath) ? null : storage.ResolvePublicUrl(row.Studio.LogoPath));
        return new(j.Id, j.Slug, j.Title!, studio, j.JobType!.Value, j.WorkMode!.Value, city?.Name, city?.StateCode,
            j.SalaryMin!.Value, j.SalaryMax!.Value, j.PayUnit!.Value, j.MinYears!.Value, j.MaxYears, j.FresherFriendly,
            j.PublishedAt!.Value, j.ExpiresAt!.Value);
    }
    private static Error Unavailable() => Error.NotFound("PublicJobs.Unavailable", "This listing is unavailable.");
    private sealed class Listing
    {
        public Job Job { get; init; } = null!;
        public StudioProfile Studio { get; init; } = null!;
    }
}
