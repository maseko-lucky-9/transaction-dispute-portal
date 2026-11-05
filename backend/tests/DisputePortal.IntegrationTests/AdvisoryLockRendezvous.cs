namespace DisputePortal.IntegrationTests;

/// <summary>
/// A <see cref="Rendezvous"/> that also counts the parties queued on an advisory lock in the test database: each caller
/// waits until every party has either arrived or is waiting for the lock. Without a lock, every party arrives, so all
/// of them pass the gate's point before any goes on: the worst interleaving, forced. With one, whoever holds it goes on
/// once the others are queued behind it, so the gate never deadlocks the lock it is testing. Postgres signals nothing
/// when a backend starts to wait, so the gate polls pg_locks for that state, as Postgres's own isolation tester does; a
/// party that never arrives or queues fails the waiters after the timeout rather than hanging the run.
/// </summary>
public sealed class AdvisoryLockRendezvous(int parties, string adminConnectionString)
{
    // Tests run one at a time (AssemblyInfo.cs) and only a submission takes an advisory lock, so any waiter is a party.
    private const string QueuedSql = """
        SELECT count(*) FROM pg_locks
        WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    private int arrived;

    public async Task ArriveAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref arrived);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        while (Volatile.Read(ref arrived) + await AdminSql.CountAsync(adminConnectionString, QueuedSql) < parties)
        {
            await Task.Delay(PollInterval, deadline.Token);
        }
    }
}
