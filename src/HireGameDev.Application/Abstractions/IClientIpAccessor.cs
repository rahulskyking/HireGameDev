using System.Net;

namespace HireGameDev.Application.Abstractions;

public interface IClientIpAccessor
{
    IPAddress? GetClientIpAddress();
}
