namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// Serialises one customer's dispute submissions (ADR-014): held from the call until the current transaction ends, so
/// the counts the dispute caps are judged on cannot change between the read and the insert.
/// </summary>
public interface ICustomerLock
{
    /// <summary>Waits until no other transaction holds <paramref name="customerId"/>'s lock, then holds it until commit or rollback.</summary>
    Task AcquireAsync(Guid customerId, CancellationToken cancellationToken);
}
