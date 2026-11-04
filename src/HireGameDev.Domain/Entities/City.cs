namespace HireGameDev.Domain.Entities;

public sealed class City
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string StateCode { get; set; } = string.Empty;

    public string CountryCode { get; set; } = "IN";

    public string? MetroGroup { get; set; }

    public string[] Aliases { get; set; } = [];

    public bool IsHub { get; set; }

    public short SortOrder { get; set; } = 100;

    public bool IsActive { get; set; } = true;
}
