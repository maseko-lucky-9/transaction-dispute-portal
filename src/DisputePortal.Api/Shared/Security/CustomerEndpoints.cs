namespace DisputePortal.Api.Shared.Security;

public static class CustomerEndpoints
{
    /// <summary>
    /// Customer-only routes: the Customer policy (an agent gets 403, a dual-role session 403 role_conflict), then the
    /// customer scope that the Owner filter reads.
    /// </summary>
    public static RouteGroupBuilder RequireCustomer(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(Policies.Customer)
            .AddEndpointFilter<CustomerScopeFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        return group;
    }
}
