namespace DisputePortal.IntegrationTests;

/// <summary>
/// Holds every caller until <c>parties</c> have arrived, then releases them together: forces an interleaving instead of
/// hoping for one. A party that never arrives fails the waiters after the timeout rather than hanging the run.
/// </summary>
public sealed class Rendezvous(int parties)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrived;

    public async Task ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref arrived) >= parties)
        {
            released.TrySetResult();
        }

        await released.Task.WaitAsync(Timeout, cancellationToken);
    }
}
