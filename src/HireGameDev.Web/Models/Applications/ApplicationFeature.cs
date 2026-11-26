using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Features.Applications;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Web.Models.Applications;

// Deliberately impossible to enable outside Development. Never use for real personal data.
public static class ApplicationFeature
{
    public static bool Enabled(IWebHostEnvironment environment) => environment.IsDevelopment();
}

public sealed record ApplicationReviewPage(Guid JobId, string? JobSlug, string? ScreeningAnswer, ApplicationReviewOffer Offer);
public sealed record ApplicantListPage(Guid JobId, Guid? StudioId, int Page, ApplicationStatus? Status, StudioApplicantPage Results);

public sealed record ApplicationRetryPage(Guid JobId, string? JobSlug, string? ScreeningAnswer, string Message);

// The return context only controls navigation; access is always rechecked by the service.
public sealed record StudioApplicantDetailPage(StudioApplicantDetail Application, Guid? StudioId, int Page, ApplicationStatus? Status);

public static class ApplicationStatusUi
{
    public static string Description(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Submitted => "Your application has been sent to the Studio.",
        ApplicationStatus.Reviewing => "The Studio has marked your application as under review.",
        ApplicationStatus.Shortlisted => "The Studio has shortlisted your application.",
        ApplicationStatus.Rejected => "The Studio has marked your application as unsuccessful.",
        ApplicationStatus.Hired => "The Studio has marked your application as hired.",
        ApplicationStatus.Withdrawn => "You withdrew this application. The Studio cannot access your snapshot.",
        _ => "Your application status is unavailable."
    };
}
