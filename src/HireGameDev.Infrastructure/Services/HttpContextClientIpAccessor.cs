using System.Net;
using HireGameDev.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace HireGameDev.Infrastructure.Services;

internal sealed class HttpContextClientIpAccessor : IClientIpAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextClientIpAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public IPAddress? GetClientIpAddress()
    {
        var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return null;
        }

        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }
}
