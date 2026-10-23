using HireGameDev.Application.Abstractions;

namespace HireGameDev.Infrastructure.Services;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
