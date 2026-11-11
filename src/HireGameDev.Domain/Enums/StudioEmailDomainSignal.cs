namespace HireGameDev.Domain.Enums;

/// <summary>
/// Signal comparing the studio Owner's verified account email domain against the studio website domain
/// and known consumer free-mail providers (Blueprint v2.0 Section 5, ADR-008, ADR-019).
/// </summary>
public enum StudioEmailDomainSignal
{
    DomainMatched = 0,
    CompanyDomainUnmatched = 1,
    CompanyDomainNoWebsite = 2,
    PersonalEmail = 3
}

public static class StudioEmailDomainSignalExtensions
{
    public static string ToDisplayLabel(this StudioEmailDomainSignal signal) => signal switch
    {
        StudioEmailDomainSignal.DomainMatched => "Company Domain Match",
        StudioEmailDomainSignal.CompanyDomainUnmatched => "Company Email · Domain Mismatch",
        StudioEmailDomainSignal.CompanyDomainNoWebsite => "Company Email · No Website Listed",
        StudioEmailDomainSignal.PersonalEmail => "Personal Email · Indie Review",
        _ => "Personal Email · Indie Review"
    };
}
