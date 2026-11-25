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
