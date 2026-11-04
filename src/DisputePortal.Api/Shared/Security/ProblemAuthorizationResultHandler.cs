using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace DisputePortal.Api.Shared.Security;

/// <summary>Counts every 403 and answers a role conflict with its own code; other refusals go through the cookie events.</summary>
public sealed class ProblemAuthorizationResultHandler(SecurityMetrics metrics) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            metrics.Record(SecurityMetrics.AuthzDenied);
            if (authorizeResult.AuthorizationFailure?.FailureReasons.Any(reason => reason.Handler is SingleRoleRequirement) == true)
            {
                await ProblemResponses.WriteAsync(context, StatusCodes.Status403Forbidden, SingleRoleRequirement.Code,
                    "This account holds both the customer and the dispute-agent role; ask an administrator to remove one.");
                return;
            }
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
