using HireGameDev.Application.Features.Studios;
using HireGameDev.Domain.Enums;

namespace HireGameDev.Web.Models.Admin;

public sealed class ReviewStudioVerificationInputModel
{
    public Guid StudioId { get; set; }

    public StudioVerificationDecision Decision { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? ReviewerFeedback { get; set; }

    public string? ReturnFilter { get; set; }
}

public sealed class AdminDashboardViewModel
{
    public required AdminStudioVerificationQueueCounts StudioCounts { get; init; }

    public int PastSlaPendingCount { get; init; }
}

public sealed class AdminStudioVerificationPageViewModel
{
    public required AdminStudioVerificationQueueModel Queue { get; init; }

    public string ActiveTabKey { get; init; } = "pending";

    public Guid? ErrorStudioId { get; init; }

    public ReviewStudioVerificationInputModel? FailedInput { get; init; }

    public static bool TryBuildSafeHttpsHref(string? rawUrl, out string safeHref)
    {
        safeHref = string.Empty;
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        safeHref = uri.AbsoluteUri;
        return true;
    }
}
