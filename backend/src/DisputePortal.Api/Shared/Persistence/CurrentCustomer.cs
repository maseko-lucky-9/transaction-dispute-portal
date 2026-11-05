namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// Request-scoped. Set once per request by <see cref="Security.CustomerScopeFilter"/> after the Customer
/// policy passed, and by the migrate seeder for each customer it seeds. Never set from request data.
/// </summary>
public sealed class CurrentCustomer : ICurrentCustomer
{
    public Guid? CustomerId { get; private set; }

    public void Set(Guid customerId) => CustomerId = customerId;
}
