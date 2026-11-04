using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// Once armed, lets the next commit succeed in the database and then fails it with a transient error, as a dropped
/// connection after COMMIT would: EF's retrying execution strategy then runs SaveChanges again.
/// </summary>
public sealed class LostCommitAcknowledgement : DbTransactionInterceptor
{
    private int armed;

    public int Fired { get; private set; }

    public void Arm() => Interlocked.Exchange(ref armed, 1);

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref armed, 0) == 1)
        {
            Fired++;
            throw new NpgsqlException("Simulated: the connection dropped after COMMIT, before the acknowledgement.", new TimeoutException());
        }

        return Task.CompletedTask;
    }
}
