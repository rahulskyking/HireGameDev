using HireGameDev.Application.Abstractions.Applications;
using HireGameDev.Application.Features.Applications;

namespace HireGameDev.Web.Models.Applications;

// Deliberately impossible to enable outside Development. Never use for real personal data.
public static class ApplicationPilot
{
    public static bool Enabled(IWebHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>("ApplicationPilot:Enabled");
}

public sealed record ApplicationReviewPage(Guid JobId, string? ScreeningAnswer, ApplicationReviewOffer Offer);
public sealed record ApplicantListPage(Guid JobId, int Page, StudioApplicantPage Results);
