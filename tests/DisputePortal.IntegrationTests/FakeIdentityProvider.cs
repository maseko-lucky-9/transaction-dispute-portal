using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DisputePortal.Api.Features.Bff;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// Keycloak's token endpoint in memory: the real OIDC handler runs the whole code + PKCE flow against it, with no
/// network. It checks the PKCE verifier and the client secret like Keycloak, signs ID tokens with its own RSA key and
/// rotates refresh tokens. It also mints back-channel logout tokens shaped like Keycloak 26.4's (captured from the real
/// one: typ logout+jwt, RS256 with the realm key's kid, iat, exp = iat + 120 s, jti, sid, sub, events, no nonce).
/// </summary>
public sealed class FakeIdentityProvider
{
    public const string Issuer = "https://idp.test/realms/dispute-portal";
    public const string ClientId = "dispute-portal-bff";

    private readonly RsaSecurityKey signingKey = new(RSA.Create(2048)) { KeyId = "fake-idp" };
    private readonly ConcurrentDictionary<string, PendingCode> codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IdpUser> refreshTokens = new(StringComparer.Ordinal);
    private int refreshCount;

    public FakeIdentityProvider(string clientSecret)
    {
        ClientSecret = clientSecret;
        Configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = Issuer + "/protocol/openid-connect/auth",
            TokenEndpoint = Issuer + "/protocol/openid-connect/token",
            EndSessionEndpoint = Issuer + "/protocol/openid-connect/logout",
        };
        Configuration.SigningKeys.Add(signingKey);
        Handler = new Responder(RespondAsync);
    }

    public string ClientSecret { get; }

    public OpenIdConnectConfiguration Configuration { get; }

    public HttpMessageHandler Handler { get; }

    /// <summary>Seconds until the access token expires; under 60 makes the next request refresh.</summary>
    public int AccessTokenLifetimeSeconds { get; set; } = 300;

    /// <summary>Answers the refresh_token grant with invalid_grant, as Keycloak does once the session has ended.</summary>
    public bool RefuseRefresh { get; set; }

    /// <summary>Roles the next refreshed ID token carries, to prove the session re-reads them.</summary>
    public IReadOnlyList<string>? RolesOnRefresh { get; set; }

    /// <summary>Answers the refresh_token grant with 503, as a restarting Keycloak would.</summary>
    public bool RefreshUnavailable { get; set; }

    /// <summary>How long a successful redemption takes. A replayed token is refused at once, so a race is decided before the winner returns.</summary>
    public TimeSpan RefreshDelay { get; set; }

    /// <summary>Signs refreshed ID tokens with a key the metadata does not list, as a forged token would be.</summary>
    public bool SignRefreshedIdTokenWithUnknownKey { get; set; }

    public int RefreshCount => refreshCount;

    /// <summary>The refresh token most recently handed out; after a rotation, the only one Keycloak still honours.</summary>
    public string? LastIssuedRefreshToken { get; private set; }

    /// <summary>Registers the code Keycloak would hand back for this authorize request.</summary>
    public void Approve(string code, IdpUser user, string nonce, string codeChallenge) =>
        codes[code] = new PendingCode(user, nonce, codeChallenge);

    public void Reset()
    {
        AccessTokenLifetimeSeconds = 300;
        RefuseRefresh = false;
        RefreshUnavailable = false;
        RefreshDelay = TimeSpan.Zero;
        SignRefreshedIdTokenWithUnknownKey = false;
        RolesOnRefresh = null;
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri?.ToString() != Configuration.TokenEndpoint)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        Dictionary<string, string> form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync())
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
        if (form.GetValueOrDefault("client_id") != ClientId || form.GetValueOrDefault("client_secret") != ClientSecret)
        {
            return Error("invalid_client");
        }

        return form.GetValueOrDefault("grant_type") switch
        {
            "authorization_code" => RedeemCode(form),
            "refresh_token" => await RefreshAsync(form),
            _ => Error("unsupported_grant_type"),
        };
    }

    private HttpResponseMessage RedeemCode(Dictionary<string, string> form)
    {
        if (!codes.TryRemove(form.GetValueOrDefault("code") ?? "", out PendingCode? pending)
            || Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form.GetValueOrDefault("code_verifier") ?? ""))) != pending.CodeChallenge)
        {
            return Error("invalid_grant");
        }

        return Tokens(pending.User, pending.Nonce);
    }

    private async Task<HttpResponseMessage> RefreshAsync(Dictionary<string, string> form)
    {
        Interlocked.Increment(ref refreshCount);
        if (RefreshUnavailable)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        // Rotation with reuse 0: a refresh token works once.
        if (RefuseRefresh || !refreshTokens.TryRemove(form.GetValueOrDefault("refresh_token") ?? "", out IdpUser? user))
        {
            return Error("invalid_grant");
        }

        await Task.Delay(RefreshDelay);
        SecurityKey? unknownKey = SignRefreshedIdTokenWithUnknownKey ? new RsaSecurityKey(RSA.Create(2048)) { KeyId = signingKey.KeyId } : null;
        return Tokens(RolesOnRefresh is null ? user : user with { Roles = RolesOnRefresh }, nonce: null, unknownKey);
    }

    private HttpResponseMessage Tokens(IdpUser user, string? nonce, SecurityKey? signingKeyOverride = null)
    {
        string refreshToken = "rt-" + Guid.NewGuid().ToString("N");
        refreshTokens[refreshToken] = user;
        LastIssuedRefreshToken = refreshToken;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new Dictionary<string, object>
            {
                ["access_token"] = "at-" + Guid.NewGuid().ToString("N"),
                ["token_type"] = "Bearer",
                ["expires_in"] = AccessTokenLifetimeSeconds,
                ["refresh_token"] = refreshToken,
                ["id_token"] = IdToken(user, nonce, signingKeyOverride ?? signingKey),
            }),
        };
    }

    private string IdToken(IdpUser user, string? nonce, SecurityKey key)
    {
        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            ["sub"] = user.Subject,
            ["name"] = user.Name,
            ["sid"] = user.SessionId,
            // Keycloak's default roles must never reach the session.
            ["roles"] = user.Roles.Append("offline_access").ToArray(),
        };
        if (nonce is not null)
        {
            claims["nonce"] = nonce;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            Claims = claims,
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }

    /// <summary>
    /// A logout token for <paramref name="sessionId"/>, signed with this provider's key unless <paramref name="key"/> is
    /// given. <paramref name="tamper"/> edits the claims before signing, to break one rule at a time.
    /// </summary>
    public string LogoutToken(string sessionId, string? subject = null, Action<Dictionary<string, object>>? tamper = null, SecurityKey? key = null)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            ["iss"] = Issuer,
            ["aud"] = ClientId,
            ["iat"] = now,
            ["exp"] = now + 120,
            ["jti"] = Guid.NewGuid().ToString(),
            ["sid"] = sessionId,
            ["typ"] = "Logout",
            ["events"] = new Dictionary<string, object> { [LogoutTokenValidator.BackchannelLogoutEvent] = new Dictionary<string, object>() },
        };
        if (subject is not null)
        {
            claims["sub"] = subject;
        }

        tamper?.Invoke(claims);
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            JsonSerializer.Serialize(claims),
            new SigningCredentials(key ?? signingKey, SecurityAlgorithms.RsaSha256),
            new Dictionary<string, object> { ["typ"] = LogoutTokenValidator.TokenType });
    }

    private static HttpResponseMessage Error(string error) =>
        new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new Dictionary<string, string> { ["error"] = error }) };

    private sealed record PendingCode(IdpUser User, string Nonce, string CodeChallenge);

    private sealed class Responder(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
