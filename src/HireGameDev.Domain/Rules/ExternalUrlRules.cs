using System.Net;
using System.Net.Sockets;

namespace HireGameDev.Domain.Rules;

/// <summary>
/// Validates and normalizes untrusted external URLs (portfolio items, game store pages, studio websites)
/// so only canonical public HTTPS URLs can be stored or rendered in anchor href attributes.
/// </summary>
public static class ExternalUrlRules
{
    public const int MaxUrlLength = 500;

    public static bool TryNormalizeHttpsUrl(
        string? rawUrl,
        out string normalizedUrl,
        out string? errorMessage)
    {
        normalizedUrl = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            errorMessage = "URL is required.";
            return false;
        }

        var trimmed = rawUrl.Trim();
        if (trimmed.Length > MaxUrlLength)
        {
            errorMessage = $"URL must be {MaxUrlLength} characters or fewer.";
            return false;
        }

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (ch <= 0x20 || char.IsControl(ch))
            {
                errorMessage = "URL cannot contain spaces or control characters.";
                return false;
            }
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            errorMessage = "Enter a valid full URL starting with https://.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "External URLs must use https://.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errorMessage = "URLs cannot contain embedded credentials.";
            return false;
        }

        var host = uri.IdnHost;
        if (string.IsNullOrWhiteSpace(host) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            !host.Contains('.', StringComparison.Ordinal) ||
            host.StartsWith('.') ||
            host.EndsWith('.'))
        {
            errorMessage = "URL must point to a valid public domain.";
            return false;
        }

        if (IPAddress.TryParse(host, out var ip) && IsNonPublicIp(ip))
        {
            errorMessage = "URL cannot point to a local or private IP address.";
            return false;
        }

        var canonical = uri.AbsoluteUri;
        if (canonical.Length > MaxUrlLength)
        {
            errorMessage = $"Normalized URL must be {MaxUrlLength} characters or fewer.";
            return false;
        }

        normalizedUrl = canonical;
        return true;
    }

    private static bool IsNonPublicIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal ||
                   ip.IsIPv6SiteLocal ||
                   ip.IsIPv6Multicast ||
                   ip.Equals(IPAddress.IPv6None) ||
                   ip.Equals(IPAddress.IPv6Any);
        }

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            // 0.0.0.0/8, 10.0.0.0/8, 100.64.0.0/10, 127.0.0.0/8, 169.254.0.0/16, 172.16.0.0/12, 192.168.0.0/16
            if (bytes[0] == 0 ||
                bytes[0] == 10 ||
                bytes[0] == 127 ||
                (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                (bytes[0] == 169 && bytes[1] == 254) ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168))
            {
                return true;
            }
        }

        return false;
    }
}
