using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Settings;

namespace HireGameDev.Application.Abstractions.Settings;

public interface ISettingsService
{
    Task<IReadOnlyList<AppSettingItem>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<int> GetIntAsync(
        string key,
        int defaultValue,
        CancellationToken cancellationToken = default);

    Task<decimal> GetDecimalAsync(
        string key,
        decimal defaultValue,
        CancellationToken cancellationToken = default);

    Task<bool> GetBoolAsync(
        string key,
        bool defaultValue,
        CancellationToken cancellationToken = default);

    Task<string> GetStringAsync(
        string key,
        string defaultValue,
        CancellationToken cancellationToken = default);

    Task<Result<AppSettingItem>> UpdateSettingAsync(
        UpdateAppSettingCommand command,
        CancellationToken cancellationToken = default);

    void InvalidateCache();
}
