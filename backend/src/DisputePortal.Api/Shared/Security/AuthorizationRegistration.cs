using Microsoft.AspNetCore.Authorization;

namespace DisputePortal.Api.Shared.Security;

public static class AuthorizationRegistration
{
    /// <summary>Deny by default: every endpoint needs a session unless it opts out with AllowAnonymous.</summary>
    public static IServiceCollection AddPortalAuthorization(this IServiceCollection services)
    {
        AuthorizationPolicy authenticated = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new SingleRoleRequirement())
            .Build();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(authenticated)
            .SetFallbackPolicy(authenticated)
            .AddPolicy(Policies.Customer, policy => policy.Combine(authenticated).RequireRole(Roles.Customer))
            .AddPolicy(Policies.DisputeAgent, policy => policy.Combine(authenticated).RequireRole(Roles.DisputeAgent));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        return services;
    }
}
