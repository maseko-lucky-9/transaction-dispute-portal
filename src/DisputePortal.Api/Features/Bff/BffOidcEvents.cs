using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Features.Bff;

public sealed class BffOidcEvents(IOptions<PublicSettings> publicSettings, TimeProvider time, ILogger<BffOidcEvents> logger)
    : OpenIdConnectEvents
{
    // Built from configuration, so the exact URIs registered in the realm match whatever Host header arrived.
    public override Task RedirectToIdentityProvider(RedirectContext context)
    {
        context.ProtocolMessage.RedirectUri = publicSettings.Value.Url(context.Options.CallbackPath);
        return Task.CompletedTask;
    }

    public override Task RedirectToIdentityProviderForSignOut(RedirectContext context)
    {
        context.ProtocolMessage.PostLogoutRedirectUri = publicSettings.Value.Url(context.Options.SignedOutCallbackPath);
        return Task.CompletedTask;
    }

    /// <summary>Trims the principal to the session's claims and keeps only the tokens the BFF needs (SaveTokens is off).</summary>
    public override Task TokenValidated(TokenValidatedContext context)
    {
        DateTimeOffset now = time.GetUtcNow();
        context.Principal = SessionPrincipal.Create(context.Principal?.Claims ?? []);
        context.Properties ??= new AuthenticationProperties();
        context.Properties.IsPersistent = false;
        SessionTokens.StartAbsoluteLifetime(context.Properties, now);
        int lifetimeSeconds = int.TryParse(context.TokenEndpointResponse?.ExpiresIn, out int seconds) ? seconds : 0;
        SessionTokens.Store(
            context.Properties,
            context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage.IdToken ?? string.Empty,
            context.TokenEndpointResponse?.RefreshToken ?? string.Empty,
            now.AddSeconds(lifetimeSeconds));
        return Task.CompletedTask;
    }

    // A cancelled or tampered sign-in is a client error with a stable code, not a 500 with a stack trace.
    public override async Task RemoteFailure(RemoteFailureContext context)
    {
        logger.LogWarning("OIDC sign-in failed: {Reason}", context.Failure?.GetType().Name);
        context.HandleResponse();
        await ProblemResponses.WriteAsync(context.HttpContext, StatusCodes.Status400BadRequest, "login_failed",
            "Sign-in could not be completed; start again from the login page.");
    }
}
