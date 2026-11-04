using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// Runs on every authenticated request. Ends a session past its absolute lifetime; when the access token is within
/// <see cref="RefreshLeeway"/> of expiry, refreshes it (once across all replicas) and re-reads the roles, so a revoked
/// user or role is noticed within one access-token lifetime (5 minutes). A refused refresh ends the session; an
/// unreachable identity provider fails the request with 503 and keeps the session.
/// </summary>
public sealed class SessionValidator(
    SessionRefreshCoordinator coordinator,
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

        if (SessionTokens.AccessTokenExpiresAt(context.Properties) is { } expiresAt && expiresAt - now > RefreshLeeway)
        {
            return;
        }

        string? refreshToken = context.Properties.GetTokenValue(SessionTokens.RefreshToken);
        string? sessionKey = SessionCookie.ReadKey(context.HttpContext, context.Options);
        RefreshOutcome outcome = refreshToken is null || sessionKey is null
            ? RefreshOutcome.Gone
            : await singleFlight.RunAsync(refreshToken, () => coordinator.RefreshAsync(
                sessionKey, refreshToken, context.Principal?.FindFirst(ClaimNames.SessionId)?.Value, context.Scheme.Name));
        await ApplyAsync(context, outcome);
    }

    private async Task ApplyAsync(CookieValidatePrincipalContext context, RefreshOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case RefreshOutcomeKind.Renewed:
                // Copied, not shared: the outcome's ticket may serve several requests at once.
                context.ReplacePrincipal(outcome.Ticket!.Principal);
                context.Properties.StoreTokens(outcome.Ticket.Properties.GetTokens());
                context.ShouldRenew = true;
                return;
            case RefreshOutcomeKind.Unavailable:
                throw new IdentityProviderUnavailableException();
            case RefreshOutcomeKind.Refused:
                metrics.Record(SecurityMetrics.SessionRefreshFailed);
                logger.LogWarning("Session refresh was refused; the session was ended");
                break;
        }

        await EndSessionAsync(context);
    }

    // Rejecting alone would leave the cookie; signing out expires it (its ticket is already gone, or deleted here).
    private static async Task EndSessionAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
