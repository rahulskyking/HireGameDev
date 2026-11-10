namespace HireGameDev.Domain.Entities;

public sealed class StudioTitle
{
    public Guid Id { get; set; }

    public Guid StudioId { get; set; }

    public StudioProfile Studio { get; set; } = null!;

    public string GameName { get; set; } = string.Empty;

    public short? ReleaseYear { get; set; }

    public string[] Platforms { get; set; } = [];

    public string? StoreUrl { get; set; }

    public long? IgdbId { get; set; }

    public short SortOrder { get; set; }
}
