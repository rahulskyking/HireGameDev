namespace HireGameDev.Application.Features.Settings;

public static class AppSettingKeys
{
    public const string ApplyPerDayLimit = "limits.apply_per_day";
    public const string MaxActiveApplicationsPerJob = "limits.max_active_applications_per_job";
    public const string FreeActiveJobsLimit = "limits.free_active_jobs";
    public const string ResumeParsePerDayLimit = "limits.resume_parse_per_day";
    public const string StudioProfileViewsPerDayLimit = "limits.profile_views_per_studio_per_day";
    public const string ReportsPerDayLimit = "limits.reports_per_day";

    public const string JobExpiryDays = "job.expiry_days";
    public const string JobSalaryMaxSpread = "job.salary_max_spread";

    public const string AntiGhostReminderDays = "antighost.reminder_days";
    public const string AntiGhostAutoDeclineDays = "antighost.autodecline_days";

    public const string MatchAlgorithmVersion = "match.algo_version";

    public const string LegalTermsVersion = "legal.terms_version";
    public const string LegalPrivacyVersion = "legal.privacy_version";
    public const string DefaultLegalVersion = "1.0";

    public const string FeatureInvites = "features.invites";
    public const string FeatureChat = "features.chat";
    public const string FeaturePayments = "features.payments";
    public const string FeatureBoost = "features.boost";
}

public sealed record AppSettingItem(
    string Key,
    string RawJsonValue,
    string? Description,
    Guid? UpdatedBy,
    DateTimeOffset UpdatedAt);

public sealed record UpdateAppSettingCommand(
    string Key,
    string JsonValue,
    Guid AdminUserId,
    string Reason,
    string? Description = null);
