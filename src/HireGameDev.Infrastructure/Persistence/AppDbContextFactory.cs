using HireGameDev.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HireGameDev.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef` CLI and Visual Studio Package Manager Console (`Add-Migration` / `Update-Database`)
/// so migrations work cleanly without booting the web host or freezing Serilog's bootstrap logger.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var webProjectPath = ResolveWebProjectPath();

        var configurationBuilder = new ConfigurationBuilder();

        if (webProjectPath is not null)
        {
            configurationBuilder
                .SetBasePath(webProjectPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
        }

        configurationBuilder
            .AddUserSecrets("hiregamedev-web-dev")
            .AddEnvironmentVariables();

        var configuration = configurationBuilder.Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=hiregamedev_dev;Username=postgres;Password=postgres";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention();

        return new AppDbContext(optionsBuilder.Options, new SystemClock());
    }

    private static string? ResolveWebProjectPath()
    {
        var current = Directory.GetCurrentDirectory();

        var candidates = new[]
        {
            current,
            Path.Combine(current, "src", "HireGameDev.Web"),
            Path.Combine(current, "..", "HireGameDev.Web")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(fullPath, "HireGameDev.Web.csproj")))
            {
                return fullPath;
            }
        }

        return null;
    }
}
