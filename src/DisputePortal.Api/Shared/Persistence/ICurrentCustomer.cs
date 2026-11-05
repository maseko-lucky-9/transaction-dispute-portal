namespace DisputePortal.Api.Shared.Persistence;

/// <summary>The customer this request acts for. Null means none: the Owner filter then returns no rows (fail closed).</summary>
public interface ICurrentCustomer
{
    Guid? CustomerId { get; }
}
