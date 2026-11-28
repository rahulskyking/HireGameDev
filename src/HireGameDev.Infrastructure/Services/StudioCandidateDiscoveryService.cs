using HireGameDev.Application.Abstractions.Discovery;
using HireGameDev.Application.Common;
using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Enums;
using HireGameDev.Domain.Rules;
using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HireGameDev.Infrastructure.Services;

// Development-only discovery. All returned fields use an explicit allowlist. Never return
// phone, email, compensation, application snapshots, NDA credits or private files.
internal sealed class StudioCandidateDiscoveryService(AppDbContext db) : IStudioCandidateDiscoveryService
{
    private const int PageSize = 20;
    private static Error Denied() => Error.Forbidden("Discovery.StudioRequired", "A verified, active Studio membership is required.");
    private static Error Missing() => Error.NotFound("Discovery.Unavailable", "This profile is unavailable.");

    // Keep actor and Studio authorization, Candidate privacy and block checks in the same
    // query that reads profile data, including when navigating directly to a profile URL.
    private IQueryable<CandidateProfile> Visible(Guid actorUserId, Guid studioId) =>
        from candidate in db.CandidateProfiles.AsNoTracking()
        join candidateUser in db.Users.AsNoTracking() on candidate.UserId equals candidateUser.Id
        where actorUserId != Guid.Empty && studioId != Guid.Empty &&
            candidate.DeletedAt == null && candidate.IsDiscoverable && candidate.Completeness >= CandidateProfileRules.DiscoverableCompletenessThreshold &&
            candidate.Availability != CandidateAvailability.NotLooking && candidateUser.IsActive && candidateUser.DeletedAt == null && candidateUser.AccountType == AccountType.Candidate &&
            db.Users.Any(actor => actor.Id == actorUserId && actor.IsActive && actor.DeletedAt == null && actor.AccountType == AccountType.Studio) &&
            db.StudioProfiles.Any(studio => studio.Id == studioId && studio.DeletedAt == null && studio.SuspendedAt == null && studio.VerificationStatus == StudioVerificationStatus.Verified) &&
            db.StudioMembers.Any(member => member.UserId == actorUserId && member.StudioId == studioId &&
                (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer)) &&
            !db.CandidateStudioBlocks.Any(b => b.CandidateId == candidate.Id && db.StudioMembers.Any(m =>
                m.UserId == actorUserId && m.StudioId == b.StudioId && db.StudioProfiles.Any(s => s.Id == m.StudioId && s.DeletedAt == null)))
        select candidate;

    private Task<bool> HasAccessAsync(Guid actorUserId, Guid studioId, CancellationToken ct) =>
        (from studio in db.StudioProfiles.AsNoTracking()
         join member in db.StudioMembers.AsNoTracking() on studio.Id equals member.StudioId
         join actor in db.Users.AsNoTracking() on member.UserId equals actor.Id
         where actorUserId != Guid.Empty && studioId != Guid.Empty && studio.Id == studioId && actor.Id == actorUserId &&
            actor.AccountType == AccountType.Studio && actor.IsActive && actor.DeletedAt == null &&
            studio.DeletedAt == null && studio.SuspendedAt == null && studio.VerificationStatus == StudioVerificationStatus.Verified &&
            (member.MemberRole == StudioMemberRole.Owner || member.MemberRole == StudioMemberRole.Recruiter || member.MemberRole == StudioMemberRole.Viewer)
         select studio.Id).AnyAsync(ct);

    public async Task<Result<CandidateDiscoveryPage>> SearchAsync(Guid actorUserId, Guid studioId, CandidateDiscoverySearch search, CancellationToken cancellationToken = default)
    {
        if (search is null || search.Page is < 1 or > 100 || search.CityId <= 0 || search.SkillId <= 0 || search.MinimumYears is < 0 or > 99.9m ||
            (search.Category.HasValue && !Enum.IsDefined(search.Category.Value)) || search.Keyword?.Length > 80 || search.Keyword?.Any(char.IsControl) == true ||
            (!string.IsNullOrWhiteSpace(search.Keyword) && search.Keyword.Trim().Length < 2))
            return Result.Failure<CandidateDiscoveryPage>(Error.Validation("Discovery.InvalidSearch", "Check your filters. Search terms must contain 2–80 characters."));
        if (!await HasAccessAsync(actorUserId, studioId, cancellationToken)) return Result.Failure<CandidateDiscoveryPage>(Denied());
        var query = Visible(actorUserId, studioId);
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var term = search.Keyword.Trim().ToLowerInvariant();
            query = query.Where(c => c.FullName.ToLower().Contains(term) || c.Headline.ToLower().Contains(term));
        }
        if (search.Category.HasValue) query = query.Where(c => c.PrimaryCategory == search.Category.Value);
        if (search.CityId.HasValue) query = query.Where(c => c.CityId == search.CityId.Value);
        if (search.SkillId.HasValue) query = query.Where(c => db.CandidateSkills.Any(s => s.CandidateId == c.Id && s.SkillId == search.SkillId.Value));
        if (search.MinimumYears.HasValue) query = query.Where(c => c.YearsExperience >= search.MinimumYears.Value);
        if (search.RemoteOnly) query = query.Where(c => c.OpenToRemote);
        var rows = await query.OrderBy(c => c.FullName).ThenBy(c => c.Id).Skip((search.Page - 1) * PageSize).Take(PageSize + 1)
            .Select(c => new CandidateDiscoveryItem(c.Id, c.FullName, c.Headline, c.PrimaryCategory, c.Seniority, c.YearsExperience,
                c.City != null ? c.City.Name : null, c.Availability, c.OpenToRemote)).ToArrayAsync(cancellationToken);
        return Result.Success(new CandidateDiscoveryPage(rows.Take(PageSize).ToArray(), search.Page, rows.Length > PageSize && search.Page < 100));
    }

    public async Task<Result<CandidateDiscoveryDetail>> GetAsync(Guid actorUserId, Guid studioId, Guid candidateId, CancellationToken cancellationToken = default)
    {
        if (candidateId == Guid.Empty) return Result.Failure<CandidateDiscoveryDetail>(Missing());
        var row = await Visible(actorUserId, studioId).Where(c => c.Id == candidateId)
            .Select(c => new { c.Id, c.FullName, c.Headline, c.PrimaryCategory, c.Seniority, c.YearsExperience,
                City = c.City != null ? c.City.Name : null, c.Availability, c.OpenToRemote, c.WillingToRelocate, c.Bio,
                Skills = (from cs in db.CandidateSkills.AsNoTracking() join skill in db.Skills.AsNoTracking() on cs.SkillId equals skill.Id
                    where cs.CandidateId == c.Id orderby skill.Name, skill.Id
                    select new CandidateDiscoverySkill(skill.Name, cs.Proficiency)).ToList()
            }).AsSingleQuery().SingleOrDefaultAsync(cancellationToken);
        if (row is null) return Result.Failure<CandidateDiscoveryDetail>(Missing());
        return Result.Success(new CandidateDiscoveryDetail(row.Id, row.FullName, row.Headline, row.PrimaryCategory,
            row.Seniority, row.YearsExperience, row.City, row.Availability, row.OpenToRemote, row.WillingToRelocate, row.Bio, row.Skills));
    }
}
