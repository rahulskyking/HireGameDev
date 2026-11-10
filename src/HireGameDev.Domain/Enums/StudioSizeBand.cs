namespace HireGameDev.Domain.Enums;

/// <summary>
/// Headcount bands for <c>studio_profiles.size_band</c> (Blueprint v2.0 Section 5:
/// <c>'1-10'</c>, <c>'11-50'</c>, <c>'51-200'</c>, <c>'200+'</c>).
/// </summary>
public enum StudioSizeBand
{
    Band1To10 = 1,
    Band11To50 = 2,
    Band51To200 = 3,
    Band200Plus = 4
}

public static class StudioSizeBandExtensions
{
    public static string ToDbValue(this StudioSizeBand band) => band switch
    {
        StudioSizeBand.Band1To10 => "1-10",
        StudioSizeBand.Band11To50 => "11-50",
        StudioSizeBand.Band51To200 => "51-200",
        StudioSizeBand.Band200Plus => "200+",
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, $"Unsupported StudioSizeBand '{band}'.")
    };

    public static StudioSizeBand FromDbValue(string dbValue)
    {
        if (TryFromDbValue(dbValue, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Cannot convert database value '{dbValue}' to {nameof(StudioSizeBand)}.");
    }

    public static bool TryFromDbValue(string? dbValue, out StudioSizeBand band)
    {
        if (!string.IsNullOrWhiteSpace(dbValue))
        {
            switch (dbValue.Trim())
            {
                case "1-10":
                    band = StudioSizeBand.Band1To10;
                    return true;
                case "11-50":
                    band = StudioSizeBand.Band11To50;
                    return true;
                case "51-200":
                    band = StudioSizeBand.Band51To200;
                    return true;
                case "200+":
                    band = StudioSizeBand.Band200Plus;
                    return true;
            }
        }

        band = default;
        return false;
    }

    public static string ToDisplayLabel(this StudioSizeBand band) => band switch
    {
        StudioSizeBand.Band1To10 => "1–10 people",
        StudioSizeBand.Band11To50 => "11–50 people",
        StudioSizeBand.Band51To200 => "51–200 people",
        StudioSizeBand.Band200Plus => "200+ people",
        _ => "1–10 people"
    };
}
