using DisputePortal.Api.Shared.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// Runs on every authenticated request. Ends a session past its absolute lifetime; when the access token is within
/// <see cref="RefreshLeeway"/> of expiry, refreshes it and re-reads the roles, so a revoked user or role is noticed
/// within one access-token lifetime (5 minutes). A failed refresh ends the session.
/// </summary>
public sealed class SessionValidator(
    ITokenRefresher refresher,
    RefreshSingleFlight singleFlight,
    TimeProvider time,
    SecurityMetrics metrics,
    ILogger<SessionValidator> logger)
{
    public static readonly TimeSpan RefreshLeeway = TimeSpan.FromSeconds(60);

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (SessionTokens.AbsoluteExpiresAt(context.Properties) <= now)
        {
            await EndSessionAsync(context);
            return;
        }

        DateTimeOffset? accessTokenExpiresAt = SessionTokens.AccessTokenExpiresAt(context.Properties);
        if (accessTokenExpiresAt is { } expiresAt && expiresAt - now > RefreshLeeway)
        {
            return;
        }

        string? refreshToken = context.Properties.GetTokenValue(SessionTokens.RefreshToken);
        RefreshedSession? refreshed = refreshToken is null
            ? null
            // Not the request's token: a cancelled request must not cancel the refresh another request is waiting on.
            : await singleFlight.RunAsync(refreshToken, () => refresher.RefreshAsync(refreshToken, CancellationToken.None));
        if (refreshed is null)
        {
            metrics.Record(SecurityMetrics.SessionRefreshFailed);
            logger.LogWarning("Session refresh failed; the session was ended");
            await EndSessionAsync(context);
            return;
        }

        string? sessionId = context.Principal?.FindFirst(ClaimNames.SessionId)?.Value;
        context.ReplacePrincipal(SessionPrincipal.Create(refreshed.Identity.Claims, sessionId));
        SessionTokens.Store(context.Properties, refreshed.IdToken, refreshed.RefreshToken, refreshed.AccessTokenExpiresAt);
        context.ShouldRenew = true;
    }

    // Rejecting alone would leave the ticket in Redis; signing out deletes it and expires the cookie.
    private static async Task EndSessionAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
