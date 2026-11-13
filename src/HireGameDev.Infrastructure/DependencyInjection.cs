using System.Security.Claims;
using HireGameDev.Application.Abstractions;
using HireGameDev.Application.Abstractions.Audit;
using HireGameDev.Application.Abstractions.Auth;
using HireGameDev.Application.Abstractions.Candidates;
using HireGameDev.Application.Abstractions.Consent;
using HireGameDev.Application.Abstractions.Email;
using HireGameDev.Application.Abstractions.Media;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Application.Abstractions.ReferenceData;
using HireGameDev.Application.Abstractions.Settings;
using HireGameDev.Application.Abstractions.Storage;
using HireGameDev.Application.Abstractions.Studios;
using HireGameDev.Infrastructure.Email;
using HireGameDev.Infrastructure.Identity;
using HireGameDev.Infrastructure.Media;
using HireGameDev.Infrastructure.Persistence;
using HireGameDev.Infrastructure.Persistence.SeedData;
using HireGameDev.Infrastructure.ResumeAi;
using HireGameDev.Infrastructure.Services;
using HireGameDev.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemClock = HireGameDev.Infrastructure.Services.SystemClock;

namespace HireGameDev.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<IClock, SystemClock>();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<IClientIpAccessor, HttpContextClientIpAccessor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. " +
                "Set it in appsettings.Development.json, user-secrets, or the ConnectionStrings__DefaultConnection environment variable.");
        }

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

            options.UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                // Blueprint Section 13.B: Identity defaults with minimum password length 10, verified unique email, and lockout.
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = true;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "hgd.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
        });

        services.ConfigureExternalCookie(options =>
        {
            options.Cookie.Name = "hgd.external";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
        });

        var externalAuth = new ExternalAuthOptions();
        configuration.GetSection(ExternalAuthOptions.SectionName).Bind(externalAuth);
        services.Configure<ExternalAuthOptions>(configuration.GetSection(ExternalAuthOptions.SectionName));

        var authBuilder = services.AddAuthentication();

        if (externalAuth.Google.IsConfigured)
        {
            authBuilder.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
            {
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.ClientId = externalAuth.Google.ClientId;
                options.ClientSecret = externalAuth.Google.ClientSecret;
                options.CallbackPath = "/signin-google";
                options.UsePkce = true;
                options.ClaimActions.MapJsonKey("email_verified", "email_verified", ClaimValueTypes.Boolean);
                options.Events.OnRemoteFailure = context =>
                {
                    var message = Uri.EscapeDataString("Google sign-in was cancelled or could not be completed.");
                    context.Response.Redirect($"/Account/Login?remoteError={message}");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        if (externalAuth.GitHub.IsConfigured)
        {
            authBuilder.AddGitHubOAuth(externalAuth.GitHub);
        }

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        var emailProvider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? "Dev";
        if (emailProvider.Equals("Resend", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
            {
                client.BaseAddress = new Uri("https://api.resend.com/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else
        {
            services.AddScoped<IEmailSender, DevEmailSender>();
        }

        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));
        services.AddScoped<IAdminBootstrapSeeder, AdminBootstrapSeeder>();
        services.AddScoped<IAuthService, IdentityAuthService>();
        services.AddScoped<IExternalAuthChallengeProvider, IdentityExternalAuthChallengeProvider>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<IReferenceDataSeeder, ReferenceDataSeeder>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IAppSettingsSeeder, AppSettingsSeeder>();
        services.AddScoped<IConsentService, ConsentService>();

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        var storageProvider = (configuration[$"{StorageOptions.SectionName}:Provider"] ?? "Local").Trim();
        if (storageProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        }
        else if (storageProvider.Equals("CloudflareR2", StringComparison.OrdinalIgnoreCase) ||
                 storageProvider.Equals("R2", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Storage:Provider 'CloudflareR2' is reserved for the future S3-compatible CloudflareR2StorageService adapter (ADR-016). " +
                "Use 'Local' in M1.6 until Cloudflare R2 credentials and adapter are enabled.");
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported Storage:Provider '{storageProvider}'. Expected 'Local' (or future 'CloudflareR2').");
        }

        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
        services.AddSingleton<IResumePdfTextExtractor, PdfPigResumeTextExtractor>();

        services.Configure<ResumeAiOptions>(configuration.GetSection(ResumeAiOptions.SectionName));
        var resumeAiProvider = (configuration[$"{ResumeAiOptions.SectionName}:Provider"] ?? "Dev").Trim();
        if (resumeAiProvider.Equals("Dev", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IResumeLlmClient, DevResumeLlmClient>();
        }
        else if (resumeAiProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IResumeLlmClient, GeminiResumeLlmClient>();
        }
        else if (resumeAiProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IResumeLlmClient, OpenAiResumeLlmClient>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported ResumeAi:Provider '{resumeAiProvider}'. Expected 'Dev', 'Gemini', or 'OpenAI'.");
        }

        services.AddScoped<ICandidateCompletenessCalculator, CandidateCompletenessCalculator>();
        services.AddScoped<IEvidenceService, EvidenceService>();
        services.AddScoped<ICandidateProfileService, CandidateProfileService>();
        services.AddScoped<ICandidatePhotoService, CandidatePhotoService>();
        services.AddScoped<ICandidateSkillsService, CandidateSkillsService>();
        services.AddScoped<ICandidateBackgroundService, CandidateBackgroundService>();
        services.AddScoped<ICandidateProofService, CandidateProofService>();
        services.AddScoped<ICandidateResumeImportService, CandidateResumeImportService>();
        services.AddScoped<ICandidateSettingsService, CandidateSettingsService>();

        services.AddOptions<JobLifecycleOptions>()
            .Bind(configuration.GetSection(JobLifecycleOptions.SectionName))
            .Validate(x => x.ValidityDays is >= 1 and <= 365 && x.FreeActiveJobs >= 1 &&
                x.PublicationsPer24Hours >= 1 && x.RepeatCooldownDays is >= 1 and <= 365 && x.MaxDraftsPerStudio >= 1,
                "Job lifecycle limits must be positive; validity and cooldown cannot exceed 365 days.")
            .ValidateOnStart();
        services.AddScoped<IJobPublishChecker, PendingJobPublishChecker>();
        services.AddScoped<IJobLifecycleService, JobLifecycleService>();

        services.AddScoped<IStudioProfileService, StudioProfileService>();
        services.AddScoped<IStudioLogoService, StudioLogoService>();
        services.AddScoped<IStudioTeamService, StudioTeamService>();
        services.AddScoped<IStudioVerificationService, StudioVerificationService>();

        return services;
    }
}
