using System.Collections.Concurrent;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// The cheap first layer: parallel requests on this replica that need the same refresh share one attempt, so only one
/// of them queues on the Redis lock (<see cref="SessionRefreshLock"/>, the layer that works across replicas).
/// Nothing is cached after completion: a later request re-reads the ticket instead.
/// </summary>
public sealed class RefreshSingleFlight
{
    private readonly ConcurrentDictionary<string, Lazy<Task<RefreshOutcome>>> flights = new(StringComparer.Ordinal);

    public async Task<RefreshOutcome> RunAsync(string refreshToken, Func<Task<RefreshOutcome>> refresh)
    {
        string key = SessionTokens.Fingerprint(refreshToken);
        Lazy<Task<RefreshOutcome>> flight = flights.GetOrAdd(key, _ => new Lazy<Task<RefreshOutcome>>(refresh));
        try
        {
            return await flight.Value;
        }
        finally
        {
            flights.TryRemove(new KeyValuePair<string, Lazy<Task<RefreshOutcome>>>(key, flight));
        }
    }
}
