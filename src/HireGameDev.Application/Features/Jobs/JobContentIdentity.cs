using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HireGameDev.Application.Features.Jobs;

public static class JobContentIdentity
{
    public static string Normalize(string? text) => string.Join(" ", (text ?? "").Normalize(NormalizationForm.FormKC)
        .ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string NormalizedTitle(JobContent content)
    {
        var title = Normalize(content.Title);
        return title.Length > 100 ? title[..100] : title;
    }

    // v1 is deliberately unchanged from M2.1b: salary/openings/skill changes must not bypass the ledger.
    public static string Fingerprint(JobContent content)
    {
        var canonical = new { Title = Normalize(content.Title), Description = Normalize(content.Description) };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v1:" + JsonSerializer.Serialize(canonical)))).ToLowerInvariant();
    }
}
