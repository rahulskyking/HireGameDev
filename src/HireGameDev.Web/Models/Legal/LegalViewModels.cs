namespace HireGameDev.Web.Models.Legal;

public sealed class LegalDocumentViewModel
{
    public string Version { get; init; } = "1.0";

    public string CanonicalUrl { get; init; } = string.Empty;
}

public sealed class GrievanceViewModel
{
    public string OfficerName { get; init; } = "Grievance Officer (HireGameDev India)";

    public string OfficerEmail { get; init; } = "grievance@hiregamedev.com";

    public int ResponseSlaDays { get; init; } = 30;

    public string CanonicalUrl { get; init; } = string.Empty;
}
