using DisputePortal.Api.Shared.Persistence;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The customer lock switched off, for the races that prove the database backstop behind it: the unique indexes still
/// decide when two submissions both get past every check. With the real lock the second racer would wait outside the
/// window those tests hold both racers in.
/// </summary>
public sealed class NoCustomerLock : ICustomerLock
{
    public Task AcquireAsync(Guid customerId, CancellationToken cancellationToken) => Task.CompletedTask;
}
