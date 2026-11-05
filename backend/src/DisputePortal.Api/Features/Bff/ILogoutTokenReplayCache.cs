namespace DisputePortal.Api.Features.Bff;

/// <summary>Remembers logout-token ids (<c>jti</c>) across replicas, so a captured token cannot be replayed.</summary>
public interface ILogoutTokenReplayCache
{
    /// <returns>True the first time this issuer's <paramref name="tokenId"/> is seen within <paramref name="window"/>; false for a replay.</returns>
    Task<bool> TryClaimAsync(string issuer, string tokenId, TimeSpan window);

    /// <summary>
    /// Forgets a claim whose token could not be acted on (the session store failed), so a retry of that token is processed
    /// rather than refused as a replay. Best effort: it never throws; a claim it cannot release expires with its window.
    /// </summary>
    Task ReleaseAsync(string issuer, string tokenId);
}
