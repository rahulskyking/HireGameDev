using HireGameDev.Application;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Infrastructure;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Portals;
using HireGameDev.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Optional git-ignored local secrets override (for Resend API key, Google/GitHub OAuth client secrets, etc.)
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        optional: true,
        reloadOnChange: true);

    builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
    builder.Services.AddHireGameDevPortalsAndCookieSecurity(builder.Configuration, builder.Environment);
    builder.Services.AddHireGameDevAuthorization();
    builder.Services.AddHireGameDevNetworkingAndRateLimiting(builder.Configuration);

    builder.Services.AddControllersWithViews(options =>
    {
        // Enforce CSRF anti-forgery verification on all unsafe HTTP methods (POST/PUT/PATCH/DELETE) by default.
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    });

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var referenceDataSeeder = scope.ServiceProvider.GetRequiredService<IReferenceDataSeeder>();
        await referenceDataSeeder.SeedAsync();

        var appSettingsSeeder = scope.ServiceProvider.GetRequiredService<IAppSettingsSeeder>();
        await appSettingsSeeder.SeedAsync();
    }

    // Must run first so RemoteIpAddress, Request.Scheme, and allowlisted Request.Host reflect trusted reverse proxies.
    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        await next();
    });

    app.UseHttpsRedirection();
    app.UseHireGameDevPortalRoutingGuard();
    app.UseStaticFiles();

    app.UseSerilogRequestLogging();

    app.UseRouting();

    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health").AllowAnonymous();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "HireGameDev.Web terminated unexpectedly during startup.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
