using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// One refresh per refresh token at a time in this process. Keycloak rotates refresh tokens with reuse 0, so two
/// parallel SPA requests replaying the same token would revoke the session; here the second one shares the first's result.
/// ponytail: per replica only; with N replicas a race across replicas is still possible. The upgrade is a Redis lock per session.
/// </summary>
public sealed class RefreshSingleFlight(TimeProvider time)
{
    // Kept briefly after completion, so a request that read the ticket just before its renewal reuses the result.
    private static readonly TimeSpan Retention = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Lazy<Task<RefreshedSession?>>> flights = new(StringComparer.Ordinal);

    public async Task<RefreshedSession?> RunAsync(string refreshToken, Func<Task<RefreshedSession?>> refresh)
    {
        // Hashed, so the dictionary never holds a usable token as a key.
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        Lazy<Task<RefreshedSession?>> flight = flights.GetOrAdd(key, _ => new Lazy<Task<RefreshedSession?>>(refresh));
        try
        {
            return await flight.Value;
        }
        finally
        {
            _ = ForgetLaterAsync(key, flight);
        }
    }

    private async Task ForgetLaterAsync(string key, Lazy<Task<RefreshedSession?>> flight)
    {
        await Task.Delay(Retention, time);
        flights.TryRemove(new KeyValuePair<string, Lazy<Task<RefreshedSession?>>>(key, flight));
    }
}
