namespace HireGameDev.Domain.Enums;

/// <summary>
/// Studio organization category stored in <c>studio_profiles.studio_type</c>
/// (<c>'indie'</c>, <c>'game_studio'</c>, <c>'outsourcing'</c>, <c>'publisher'</c>, <c>'other'</c>).
/// </summary>
public enum StudioType
{
    Indie = 0,
    GameStudio = 1,
    Outsourcing = 2,
    Publisher = 3,
    Other = 4
}

public static class StudioTypeExtensions
{
    public static string ToDisplayLabel(this StudioType studioType) => studioType switch
    {
        StudioType.Indie => "Indie Studio",
        StudioType.GameStudio => "Game Studio",
        StudioType.Outsourcing => "Co-Dev / Outsourcing",
        StudioType.Publisher => "Publisher",
        StudioType.Other => "Other",
        _ => "Game Studio"
    };
}
