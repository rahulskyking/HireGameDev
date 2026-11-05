using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace HireGameDev.Web.RateLimiting;

public static class RateLimitPolicies
{
    /// <summary>
    /// Blueprint Section 13.A: 5 attempts per 15 minutes per client IP for Login, ForgotPassword, and ResendConfirmation.
    /// </summary>
    public const string AuthLogin = "auth-login";

    /// <summary>
    /// Blueprint Section 13.A: 5 attempts per hour per client IP for Register and External Registration flows.
    /// </summary>
    public const string AuthRegister = "auth-register";

    public static IServiceCollection AddHireGameDevNetworkingAndRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Process X-Forwarded-For and X-Forwarded-Proto ONLY from trusted reverse proxies (loopback Nginx by default).
            // Never enable X-Forwarded-Host without an explicit AllowedHosts allowlist to prevent host-header link spoofing.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            var forwardLimit = configuration.GetValue<int?>("Networking:ForwardLimit") ?? 1;
            options.ForwardLimit = Math.Clamp(forwardLimit, 1, 10);

            // Loopback (127.0.0.0/8 and ::1) is already included by ASP.NET Core defaults for same-host Nginx.
            // Optionally register additional trusted proxy IPs or private CIDR ranges from configuration.
            var trustedProxies = configuration.GetSection("Networking:TrustedProxies").Get<string[]>() ?? [];
            foreach (var rawIp in trustedProxies)
            {
                if (IPAddress.TryParse(rawIp?.Trim(), out var proxyIp))
                {
                    options.KnownProxies.Add(proxyIp);
                }
            }

            var trustedNetworks = configuration.GetSection("Networking:TrustedNetworks").Get<string[]>() ?? [];
            foreach (var rawCidr in trustedNetworks)
            {
                if (TryParseCidr(rawCidr, out var network))
                {
                    options.KnownNetworks.Add(network!);
                }
            }
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var retryAfterSeconds = 60;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                }

                context.HttpContext.Response.Headers["Retry-After"] =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HireGameDev.RateLimiting");

                logger.LogWarning(
                    "Rate limit exceeded on {Method} {Path}. Retry-After: {RetryAfterSeconds}s.",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path,
                    retryAfterSeconds);

                var accept = context.HttpContext.Request.Headers.Accept.ToString();
                if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new
                        {
                            error = "too_many_requests",
                            message = $"Too many requests. Please wait {retryAfterSeconds} seconds before trying again.",
                            retryAfterSeconds
                        },
                        cancellationToken);
                }
                else
                {
                    context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                    await context.HttpContext.Response.WriteAsync(
                        $"Too many requests. Please wait {retryAfterSeconds} seconds before trying again.",
                        cancellationToken);
                }
            };

            options.AddPolicy(AuthLogin, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: BuildClientPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(15),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(AuthRegister, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: BuildClientPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    private static string BuildClientPartitionKey(HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        // For public IPv6 clients (e.g. Indian mobile/ISP /64 allocations), bucket by /64 network prefix
        // so address rotation within a single /64 subnet cannot bypass per-IP rate limits.
        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && !IPAddress.IsLoopback(ip))
        {
            var bytes = ip.GetAddressBytes();
            for (var i = 8; i < 16; i++)
            {
                bytes[i] = 0;
            }

            return $"{new IPAddress(bytes)}/64";
        }

        return ip.ToString();
    }

    private static bool TryParseCidr(
        string? rawCidr,
        out Microsoft.AspNetCore.HttpOverrides.IPNetwork? network)
    {
        network = null;
        if (string.IsNullOrWhiteSpace(rawCidr))
        {
            return false;
        }

        var parts = rawCidr.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var ip) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefixLength))
        {
            return false;
        }

        var maxPrefix = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefix)
        {
            return false;
        }

        network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(ip, prefixLength);
        return true;
    }
}
