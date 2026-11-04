using DisputePortal.Api.Shared.Errors;
using StackExchange.Redis;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// One refresh per refresh token across all replicas: Keycloak rotates refresh tokens with reuse 0, so a second
/// redemption of the same token is refused, and the replica that got the refusal would end a session another replica
/// had just refreshed. SET NX PX with an owner token; only the owner releases it (StackExchange.Redis LockTake/LockRelease).
/// </summary>
public sealed class SessionRefreshLock(IConnectionMultiplexer redis, TimeProvider time)
{
    public const string KeyPrefix = "bff:refresh:";

    /// <summary>
    /// Longer than the worst refresh (a cold metadata fetch, 10 s back-channel timeout, plus the token call, 10 s total),
    /// so the lock never lapses mid-refresh; it only matters if the holder dies.
    /// </summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <returns>The held lock, or null when another replica still held it after <see cref="WaitLimit"/>.</returns>
    public async Task<Lease?> AcquireAsync(string refreshToken)
    {
        IDatabase database = redis.GetDatabase();
        RedisKey key = KeyPrefix + SessionTokens.Fingerprint(refreshToken);
        RedisValue owner = Guid.NewGuid().ToString("N");
        DateTimeOffset deadline = time.GetUtcNow() + WaitLimit;
        while (!await RunAsync(() => database.LockTakeAsync(key, owner, Expiry)))
        {
            if (time.GetUtcNow() >= deadline)
            {
                return null;
            }

            await Task.Delay(PollInterval, time);
        }

        return new Lease(database, key, owner);
    }

    private static async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
        {
            throw new SessionStoreUnavailableException(exception);
        }
    }

    public sealed class Lease(IDatabase database, RedisKey key, RedisValue owner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await database.LockReleaseAsync(key, owner);
            }
            catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
            {
                // The lock expires on its own; a failed release only delays the next refresh of this token.
            }
        }
    }
}
