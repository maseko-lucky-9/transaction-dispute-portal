using System.Security.Claims;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.UnitTests;

public sealed class AuthorizationPolicyTests
{
    private readonly ServiceProvider services = new ServiceCollection().AddLogging().AddPortalAuthorization().BuildServiceProvider();

    private IAuthorizationService Authorization => services.GetRequiredService<IAuthorizationService>();

    [Theory]
    [InlineData(Policies.Customer, new[] { Roles.Customer }, true)]
    [InlineData(Policies.Customer, new[] { Roles.DisputeAgent }, false)]
    [InlineData(Policies.Customer, new string[0], false)]
    [InlineData(Policies.DisputeAgent, new[] { Roles.DisputeAgent }, true)]
    [InlineData(Policies.DisputeAgent, new[] { Roles.Customer }, false)]
    [InlineData(Policies.Customer, new[] { Roles.Customer, Roles.DisputeAgent }, false)]
    [InlineData(Policies.DisputeAgent, new[] { Roles.Customer, Roles.DisputeAgent }, false)]
    public async Task EachPolicyAdmitsOnlyItsSingleRole(string policy, string[] roles, bool allowed)
    {
        AuthorizationResult result = await Authorization.AuthorizeAsync(Principal(roles), policy);

        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public async Task BothRolesFailWithTheRoleConflictReason()
    {
        AuthorizationResult result = await Authorization.AuthorizeAsync(Principal([Roles.Customer, Roles.DisputeAgent]), Policies.Customer);

        Assert.Contains(result.Failure!.FailureReasons, reason => reason.Handler is SingleRoleRequirement);
    }

    // Deny by default: an endpoint without its own policy still needs a session, and a single role.
    [Fact]
    public async Task TheFallbackPolicyRefusesAnonymousAndDualRoleUsers()
    {
        AuthorizationPolicy fallback = (await services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync())!;

        Assert.False((await Authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), fallback)).Succeeded);
        Assert.False((await Authorization.AuthorizeAsync(Principal([Roles.Customer, Roles.DisputeAgent]), fallback)).Succeeded);
        Assert.True((await Authorization.AuthorizeAsync(Principal([Roles.Customer]), fallback)).Succeeded);
    }

    private static ClaimsPrincipal Principal(string[] roles) =>
        SessionPrincipal.Create([new Claim("sub", "user-1"), .. roles.Select(role => new Claim("roles", role))]);
}
