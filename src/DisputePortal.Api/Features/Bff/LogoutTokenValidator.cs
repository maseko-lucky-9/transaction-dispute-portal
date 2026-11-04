using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// Verifies a back-channel logout token (OpenID Connect Back-Channel Logout 1.0, section 2.6) against the login handler's
/// own OIDC configuration: the same cached signing keys, its issuer (Keycloak's public URL) and our client id, so there is
/// no second trust root. The cheap checks run first and only a correctly signed token reaches Redis (the jti claim).
/// </summary>
public sealed class LogoutTokenValidator(IOptionsMonitor<OpenIdConnectOptions> oidcOptions, ILogoutTokenReplayCache replayCache, TimeProvider time)
{
    public const string BackchannelLogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    /// <summary>The JWS <c>typ</c> Keycloak sets (explicit typing, spec section 2.4): an ID or access token cannot pass as one.</summary>
    public const string TokenType = "logout+jwt";

    /// <summary>Keycloak's are about 1 KB; anything longer is refused before it is parsed.</summary>
    public const int MaxTokenLength = 8 * 1024;

    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>Keycloak signs the token as it ends the session and sends it at once; an older one is stale or replayed.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    /// <summary>A jti is remembered for as long as its <c>iat</c> could still pass, so every replay inside the window is caught.</summary>
    public static readonly TimeSpan ReplayWindow = MaxAge + ClockSkew + ClockSkew;

    private const int MaxIdLength = 256;
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();
    private static readonly string[] Algorithms = [SecurityAlgorithms.RsaSha256];

    private readonly JsonWebTokenHandler handler = new() { MapInboundClaims = false };

    public async Task<LogoutTokenResult> ValidateAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token) || Unverified(token) is not { } unverified)
        {
            return LogoutTokenResult.Refused(LogoutTokenFailure.Malformed);
        }

        // Before any key is tried: "none", and every algorithm outside the expected set, is refused outright.
        if (!Algorithms.Contains(unverified.Alg, StringComparer.Ordinal))
        {
            return LogoutTokenResult.Refused(LogoutTokenFailure.Algorithm);
        }

        OpenIdConnectOptions oidc = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        OpenIdConnectConfiguration configuration = await IdentityProviderMetadata.GetAsync(oidc, cancellationToken);
        TokenValidationResult verified = await handler.ValidateTokenAsync(token, Parameters(oidc, configuration));
        if (!verified.IsValid)
        {
            // Keycloak may have rotated its keys: the next token is checked against a fresh key set. The manager throttles
            // the refetch (at most one per refresh interval), so an unknown kid cannot make us hammer Keycloak.
            if (verified.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                oidc.ConfigurationManager!.RequestRefresh();
            }

            return LogoutTokenResult.Refused(Classify(verified.Exception));
        }

        using JsonDocument payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(((JsonWebToken)verified.SecurityToken).EncodedPayload));
        if (CheckClaims(payload.RootElement) is { } failure)
        {
            return LogoutTokenResult.Refused(failure);
        }

        string sessionId = Identifier(payload.RootElement, "sid")!;
        string tokenId = Identifier(payload.RootElement, "jti")!;
        return await replayCache.TryClaimAsync(verified.Issuer, tokenId, ReplayWindow)
            ? LogoutTokenResult.Accepted(sessionId, verified.Issuer, tokenId)
            : LogoutTokenResult.Refused(LogoutTokenFailure.Replayed);
    }

    private JsonWebToken? Unverified(string token)
    {
        if (token.Length > MaxTokenLength || !handler.CanReadToken(token))
        {
            return null;
        }

        try
        {
            return handler.ReadJsonWebToken(token);
        }
        catch (Exception exception) when (exception is SecurityTokenMalformedException or ArgumentException)
        {
            return null;
        }
    }

    // Lifetime is checked below, with the app's clock: exp, iat (bounded age) and an optional nbf.
    private static TokenValidationParameters Parameters(OpenIdConnectOptions oidc, OpenIdConnectConfiguration configuration) => new()
    {
        ValidIssuer = oidc.TokenValidationParameters.ValidIssuer,
        ValidAudience = oidc.ClientId,
        IssuerSigningKeys = configuration.SigningKeys,
        ValidAlgorithms = Algorithms,
        ValidTypes = [TokenType],
        RequireSignedTokens = true,
        RequireAudience = true,
        TryAllIssuerSigningKeys = false,
        ValidateLifetime = false,
        RequireExpirationTime = false,
    };

    private static LogoutTokenFailure Classify(Exception? exception) => exception switch
    {
        SecurityTokenInvalidAlgorithmException => LogoutTokenFailure.Algorithm,
        SecurityTokenInvalidTypeException => LogoutTokenFailure.Type,
        SecurityTokenInvalidSignatureException => LogoutTokenFailure.Signature,
        SecurityTokenInvalidIssuerException => LogoutTokenFailure.Issuer,
        SecurityTokenInvalidAudienceException => LogoutTokenFailure.Audience,
        _ => LogoutTokenFailure.Malformed,
    };

    private LogoutTokenFailure? CheckClaims(JsonElement payload)
    {
        if (CheckTimes(payload) is { } failure)
        {
            return failure;
        }

        if (!HasLogoutEvent(payload))
        {
            return LogoutTokenFailure.Events;
        }

        // An ID token carries a nonce; a logout token must not, so one can never be passed off as the other.
        if (payload.TryGetProperty("nonce", out _))
        {
            return LogoutTokenFailure.Nonce;
        }

        if (Identifier(payload, "jti") is null)
        {
            return LogoutTokenFailure.TokenId;
        }

        // The realm sets backchannel.logout.session.required, so a token without a sid is not one Keycloak sent us.
        return Identifier(payload, "sid") is null ? LogoutTokenFailure.SessionId : null;
    }

    private LogoutTokenFailure? CheckTimes(JsonElement payload)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (!TryNumericDate(payload, "iat", required: true, out DateTimeOffset? issuedAt)
            || issuedAt > now + ClockSkew
            || issuedAt < now - MaxAge - ClockSkew)
        {
            return LogoutTokenFailure.IssuedAt;
        }

        // Required by the spec (section 2.4) and always sent by Keycloak (iat + 120 s).
        if (!TryNumericDate(payload, "exp", required: true, out DateTimeOffset? expires) || expires <= now - ClockSkew)
        {
            return LogoutTokenFailure.Expired;
        }

        if (!TryNumericDate(payload, "nbf", required: false, out DateTimeOffset? notBefore) || notBefore > now + ClockSkew)
        {
            return LogoutTokenFailure.NotYetValid;
        }

        return null;
    }

    /// <returns>False when the claim is malformed, or missing while <paramref name="required"/>.</returns>
    private static bool TryNumericDate(JsonElement payload, string name, bool required, out DateTimeOffset? value)
    {
        value = null;
        if (!payload.TryGetProperty(name, out JsonElement claim))
        {
            return !required;
        }

        if (claim.ValueKind != JsonValueKind.Number || !claim.TryGetInt64(out long seconds) || seconds < 0 || seconds > MaxUnixSeconds)
        {
            return false;
        }

        value = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    // The member's value must be a JSON object (spec section 2.4), normally the empty one.
    private static bool HasLogoutEvent(JsonElement payload) =>
        payload.TryGetProperty("events", out JsonElement events)
        && events.ValueKind == JsonValueKind.Object
        && events.TryGetProperty(BackchannelLogoutEvent, out JsonElement logout)
        && logout.ValueKind == JsonValueKind.Object;

    private static string? Identifier(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out JsonElement claim)
        && claim.ValueKind == JsonValueKind.String
        && claim.GetString() is { Length: > 0 and <= MaxIdLength } value
            ? value
            : null;
}
