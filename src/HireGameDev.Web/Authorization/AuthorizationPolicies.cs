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
                      .RequireRole(HireGameDevRoleNames.Candidate)
                      .RequireClaim(HireGameDevClaimTypes.AccountType, HireGameDevRoleNames.Candidate))
            .AddPolicy(StudioMember, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(HireGameDevRoleNames.Studio)
                      .RequireClaim(HireGameDevClaimTypes.AccountType, HireGameDevRoleNames.Studio))
            .AddPolicy(AdminOnly, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(HireGameDevRoleNames.Admin));

        return services;
    }
}
