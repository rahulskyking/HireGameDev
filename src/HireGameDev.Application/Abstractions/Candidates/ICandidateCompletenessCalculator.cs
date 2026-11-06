using HireGameDev.Domain.Entities;
using HireGameDev.Domain.Rules;

namespace HireGameDev.Application.Abstractions.Candidates;

/// <summary>
/// Optional in-memory count overrides used inside transactional Save* methods before SaveChangesAsync()
/// flushes added/removed child rows to PostgreSQL. Any null property is queried directly from the database.
/// </summary>
public sealed record CandidateCompletenessCountOverrides(
    int? SkillCount = null,
    int? ExperienceCount = null,
    int? EducationCount = null,
    int? ShippedTitleCount = null,
    int? PortfolioLinkCount = null);

/// <summary>
/// Centralized profile completeness calculator shared by all candidate profile services.
/// </summary>
public interface ICandidateCompletenessCalculator
{
    Task<CandidateCompletenessBreakdown> CalculateAsync(
        CandidateProfile profile,
        bool isPersisted = true,
        CandidateCompletenessCountOverrides? overrides = null,
        CancellationToken cancellationToken = default);

    Task<CandidateCompletenessBreakdown> RecalculateAndApplyAsync(
        CandidateProfile profile,
        CandidateCompletenessCountOverrides? overrides = null,
        CancellationToken cancellationToken = default);
}
