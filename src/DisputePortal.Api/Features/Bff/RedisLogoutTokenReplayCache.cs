using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Security;
using StackExchange.Redis;

namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// SET NX EX on <c>bff:logout-jti:&lt;SHA-256 of issuer and jti&gt;</c>: whichever replica sees a jti first claims it; the key
/// expires with the window in which the token's <c>iat</c> could still pass. Redis down fails closed (503).
/// </summary>
public sealed class RedisLogoutTokenReplayCache(IConnectionMultiplexer redis) : ILogoutTokenReplayCache
{
    public const string KeyPrefix = "bff:logout-jti:";

    public async Task<bool> TryClaimAsync(string issuer, string tokenId, TimeSpan window)
    {
        RedisKey key = KeyPrefix + SessionTokens.Fingerprint(issuer + "\n" + tokenId);
        try
        {
            return await redis.GetDatabase().StringSetAsync(key, 1, window, When.NotExists);
        }
        catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
        {
            throw new SessionStoreUnavailableException(exception);
        }
    }
}
