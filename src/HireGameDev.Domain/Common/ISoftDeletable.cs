namespace HireGameDev.Domain.Common;

public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}
