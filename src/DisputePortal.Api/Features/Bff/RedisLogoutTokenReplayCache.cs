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
        try
        {
            return await redis.GetDatabase().StringSetAsync(Key(issuer, tokenId), 1, window, When.NotExists);
        }
        catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
        {
            throw new SessionStoreUnavailableException(exception);
        }
    }

    public async Task ReleaseAsync(string issuer, string tokenId)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key(issuer, tokenId));
        }
        catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
        {
            // Best effort, by contract: the caller is already answering 503 for the original failure, and this claim
            // simply expires with its window.
        }
    }

    public static RedisKey Key(string issuer, string tokenId) => KeyPrefix + SessionTokens.Fingerprint(issuer + "\n" + tokenId);
}
