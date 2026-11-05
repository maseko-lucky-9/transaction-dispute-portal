using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// Once armed, fails the next INSERT into disputes before it reaches the database with a transient error, as a dropped
/// connection would: EF's retrying execution strategy then runs the whole submit transaction again.
/// </summary>
public sealed class TransientInsertFailure : DbCommandInterceptor
{
    private int armed;

    public int Fired { get; private set; }

    public void Arm() => Interlocked.Exchange(ref armed, 1);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("INSERT INTO disputes", StringComparison.Ordinal) && Interlocked.Exchange(ref armed, 0) == 1)
        {
            Fired++;
            throw new NpgsqlException("Simulated: the connection dropped before the INSERT.", new TimeoutException());
        }

        return ValueTask.FromResult(result);
    }
}
