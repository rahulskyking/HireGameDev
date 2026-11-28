using System.Text.Json;
using HireGameDev.Application.Abstractions.Plans;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Settings;

namespace HireGameDev.Infrastructure.Services;

// A draft for future implementation only, not quota enforcement. Reuses the audited
// settings service, including its per-instance cache; never use this cache for enforcement.
internal sealed class SearchAllowanceDraftService(ISettingsService settings) : ISearchAllowanceDraftService
{
    private const string Key = "plans.free_search_allowance_draft";
    private static bool Valid(int? n) => !n.HasValue || n.Value is >= 1 and <= 10000;
    private static Error Invalid() => Error.Validation("Plans.InvalidSearchDraft", "Enter whole numbers from 1 to 10000, or leave blank, and provide a reason.");
    public async Task<Result<SearchAllowanceDraft>> GetAsync(CancellationToken cancellationToken = default)
    {
        var item = (await settings.GetAllAsync(cancellationToken)).FirstOrDefault(x => string.Equals(x.Key, Key, StringComparison.OrdinalIgnoreCase));
        if (item is null) return Result.Success(new SearchAllowanceDraft(null, null));
        try
        {
            using var document = JsonDocument.Parse(item.RawJsonValue);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("CandidateDaily", out var candidate) || !root.TryGetProperty("StudioDaily", out var studio))
                return Result.Failure<SearchAllowanceDraft>(Invalid());
            static int? Read(JsonElement element) => element.ValueKind == JsonValueKind.Null ? null : element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) ? number : -1;
            var candidateDaily = Read(candidate); var studioDaily = Read(studio);
            return Valid(candidateDaily) && Valid(studioDaily)
                ? Result.Success(new SearchAllowanceDraft(candidateDaily, studioDaily))
                : Result.Failure<SearchAllowanceDraft>(Invalid());
        }
        catch (JsonException) { return Result.Failure<SearchAllowanceDraft>(Invalid()); }
    }

    public async Task<Result> UpdateAsync(Guid actorUserId, SearchAllowanceDraft draft, string reason, CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || draft is null || !Valid(draft.CandidateDaily) || !Valid(draft.StudioDaily) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result.Failure(Invalid());
        var result = await settings.UpdateSettingAsync(new UpdateAppSettingCommand(Key, JsonSerializer.Serialize(draft), actorUserId, reason.Trim(),
            "Draft search limits only; not connected to counting or enforcement."), cancellationToken);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
