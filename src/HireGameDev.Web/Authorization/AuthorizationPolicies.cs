using HireGameDev.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace HireGameDev.Web.Authorization;

public static class AuthorizationPolicies
{
    public const string CandidateOnly = "CandidateOnly";
    public const string StudioMember = "StudioMember";
    public const string AdminOnly = "AdminOnly";

    public static IServiceCollection AddHireGameDevAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(CandidateOnly, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(HireGameDevClaimTypes.AccountType, "candidate"))
            .AddPolicy(StudioMember, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(HireGameDevClaimTypes.AccountType, "studio"))
            .AddPolicy(AdminOnly, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(HireGameDevClaimTypes.AccountType, "admin"));

        return services;
    }
}
