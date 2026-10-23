namespace HireGameDev.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
