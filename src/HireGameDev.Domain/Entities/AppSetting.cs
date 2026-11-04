namespace HireGameDev.Domain.Entities;

/// <summary>
/// Runtime-configurable platform setting stored as JSONB in <c>app_settings</c>.
/// Read via a 60-second cached snapshot so Admin edits take effect without redeploying.
/// </summary>
public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = "null";

    public string? Description { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
