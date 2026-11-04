using Microsoft.AspNetCore.Authorization;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// A session holding both the customer and the dispute-agent role is refused everywhere (403 <c>role_conflict</c>):
/// an agent must never act on disputes as their own customer. Self-handling, so it needs no registration.
/// </summary>
public sealed class SingleRoleRequirement : AuthorizationHandler<SingleRoleRequirement>, IAuthorizationRequirement
{
    public const string Code = "role_conflict";

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SingleRoleRequirement requirement)
    {
        if (context.User.IsInRole(Roles.Customer) && context.User.IsInRole(Roles.DisputeAgent))
        {
            context.Fail(new AuthorizationFailureReason(this, "The session holds both the customer and the dispute-agent role."));
        }
        else
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
