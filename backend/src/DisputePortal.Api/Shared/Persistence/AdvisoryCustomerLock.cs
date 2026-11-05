using DisputePortal.Api.Shared.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// A Postgres transaction-scoped advisory lock on the customer id, taken on the context's own connection: COMMIT or
/// ROLLBACK releases it, never application code, so a request that dies cannot leave it held.
/// </summary>
public sealed class AdvisoryCustomerLock(DisputePortalDbContext db) : ICustomerLock
{
    public async Task AcquireAsync(Guid customerId, CancellationToken cancellationToken)
    {
        // Outside a transaction the lock would be released when the statement ends, and protect nothing.
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The customer lock lasts until the transaction ends, so it needs one.");
        }

        string key = customerId.ToString("D");
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
        catch (PostgresException timeout) when (timeout.SqlState == PostgresErrorCodes.QueryCanceled)
        {
            // The connection's statement_timeout cancelled the wait: a holder kept the customer's lock longer than the
            // timeout. That is an overloaded or stalled database, so answer 503 like any other timeout, and do not retry.
            throw new DatabaseUnavailableException(timeout);
        }
    }
}
