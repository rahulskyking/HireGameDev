namespace HireGameDev.Domain.Enums;

/// <summary>
/// Candidate job-search availability stored as snake_case text in PostgreSQL:
/// 'actively_looking', 'open_to_offers', 'not_looking'.
/// </summary>
public enum CandidateAvailability
{
    ActivelyLooking = 0,
    OpenToOffers = 1,
    NotLooking = 2
}
