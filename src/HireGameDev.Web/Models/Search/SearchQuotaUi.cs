using System.Security.Cryptography;
using System.Text.Json;
using HireGameDev.Application.Abstractions.Discovery;
using HireGameDev.Application.Features.Jobs;
using Microsoft.AspNetCore.Http;

namespace HireGameDev.Web.Models.Search;

public sealed record SearchQuotaConfirmPage(string PostUrl, IReadOnlyList<KeyValuePair<string, string>> Fields, int Used, int Limit);
public sealed record SearchQuotaReachedPage(string Message, string BackUrl);
public static class SearchQuotaUi
{
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    public static string Fingerprint(PublicJobSearch s) => Hash(new {
        Keyword = s.Keyword?.Trim().ToLowerInvariant() ?? "", s.StudioId, s.Category, s.JobType, s.WorkMode, s.CityId, s.ExperienceYears,
        Skills = (s.SkillIds ?? Array.Empty<int>()).Distinct().OrderBy(x => x).ToArray(), s.FresherFriendlyOnly });
    public static string Fingerprint(CandidateDiscoverySearch s) => Hash(new {
        Keyword = s.Keyword?.Trim().ToLowerInvariant() ?? "", s.Category, s.CityId, s.SkillId, s.MinimumYears, s.RemoteOnly });
    public static bool IsFiltered(PublicJobSearch s) => !string.IsNullOrWhiteSpace(s.Keyword) || s.StudioId.HasValue ||
        s.Category.HasValue || s.JobType.HasValue || s.WorkMode.HasValue || s.CityId.HasValue || s.ExperienceYears.HasValue ||
        (s.SkillIds?.Count ?? 0) > 0 || s.FresherFriendlyOnly;
    public static bool IsFiltered(CandidateDiscoverySearch s) => !string.IsNullOrWhiteSpace(s.Keyword) || s.Category.HasValue || s.CityId.HasValue || s.SkillId.HasValue || s.MinimumYears.HasValue || s.RemoteOnly;
    public static IReadOnlyList<KeyValuePair<string, string>> Fields(IQueryCollection query, params string[] allowed) => query
        .Where(x => allowed.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
        .SelectMany(x => x.Value.Select(v => new KeyValuePair<string, string>(x.Key, v ?? ""))).ToArray();
}
