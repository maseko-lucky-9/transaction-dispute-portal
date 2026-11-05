using Microsoft.AspNetCore.Authentication;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// Refreshes a session's tokens safely across replicas. Under the Redis refresh lock it re-reads the stored ticket:
/// if another replica already rotated the tokens, it adopts them instead of replaying a spent refresh token. A refusal
/// deletes the ticket only if it still holds the token that was refused (compare-and-delete).
/// </summary>
public sealed class SessionRefreshCoordinator(ITokenRefresher refresher, SessionRefreshLock refreshLock, RedisTicketStore store)
{
    public async Task<RefreshOutcome> RefreshAsync(string sessionKey, string refreshToken, string? sessionId, string scheme)
    {
        await using SessionRefreshLock.Lease? lease = await refreshLock.AcquireAsync(refreshToken);
        AuthenticationTicket? stored = await store.RetrieveAsync(sessionKey);
        if (stored is null)
        {
            return RefreshOutcome.Gone;
        }

        if (!HoldsRefreshToken(stored, refreshToken))
        {
            return RefreshOutcome.Renewed(stored);
        }

        if (lease is null)
        {
            return RefreshOutcome.Unavailable;
        }

        // Not the request's token: a cancelled request must not cancel the refresh other requests are waiting on.
        RefreshResult result = await refresher.RefreshAsync(refreshToken, CancellationToken.None);
        return result.Status switch
        {
            RefreshStatus.Refreshed => RefreshOutcome.Renewed(await SaveAsync(sessionKey, stored, result.Session!, sessionId, scheme)),
            RefreshStatus.Refused => await store.RemoveIfAsync(sessionKey, ticket => HoldsRefreshToken(ticket, refreshToken)) is { } kept
                ? RefreshOutcome.Renewed(kept)
                : RefreshOutcome.Refused,
            _ => RefreshOutcome.Unavailable,
        };
    }

    // Written before the lock is released, so a replica waiting on the lock reads the rotated tokens, not the spent ones.
    private async Task<AuthenticationTicket> SaveAsync(
        string sessionKey, AuthenticationTicket stored, RefreshedSession refreshed, string? sessionId, string scheme)
    {
        AuthenticationTicket ticket = new(SessionPrincipal.Create(refreshed.Identity.Claims, sessionId), stored.Properties, scheme);
        SessionTokens.Store(ticket.Properties, refreshed.IdToken, refreshed.RefreshToken, refreshed.AccessTokenExpiresAt);
        await store.RenewAsync(sessionKey, ticket);
        return ticket;
    }

    private static bool HoldsRefreshToken(AuthenticationTicket ticket, string refreshToken) =>
        string.Equals(ticket.Properties.GetTokenValue(SessionTokens.RefreshToken), refreshToken, StringComparison.Ordinal);
}
