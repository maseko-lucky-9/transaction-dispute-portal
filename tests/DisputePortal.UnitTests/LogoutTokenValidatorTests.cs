using System.Security.Cryptography;
using System.Text.Json;
using DisputePortal.Api.Features.Bff;
using DisputePortal.Api.Shared.Errors;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DisputePortal.UnitTests;

/// <summary>
/// One negative test per rule of OpenID Connect Back-Channel Logout 1.0 section 2.6, each against a token that is valid in
/// every other respect (signed by the login handler's key, right issuer, audience, type, events, fresh iat, jti and sid).
/// </summary>
public sealed class LogoutTokenValidatorTests
{
    private const string Issuer = "https://idp.test/realms/dispute-portal";
    private const string ClientId = "dispute-portal-bff";
    private const string SessionId = "kc-session-1";

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly RsaSecurityKey signingKey = new(RSA.Create(2048)) { KeyId = "kc-key" };
    private readonly FixedClock clock = new(Now);
    private readonly RecordingReplayCache replay = new();
    private readonly IConfigurationManager<OpenIdConnectConfiguration> configurationManager = Substitute.For<IConfigurationManager<OpenIdConnectConfiguration>>();
    private readonly LogoutTokenValidator validator;

    public LogoutTokenValidatorTests()
    {
        OpenIdConnectConfiguration configuration = new() { Issuer = Issuer };
        configuration.SigningKeys.Add(signingKey);
        configurationManager.GetConfigurationAsync(Arg.Any<CancellationToken>()).Returns(configuration);
        OpenIdConnectOptions options = new() { ClientId = ClientId, ConfigurationManager = configurationManager };
        options.TokenValidationParameters.ValidIssuer = Issuer;
        IOptionsMonitor<OpenIdConnectOptions> monitor = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        monitor.Get(OpenIdConnectDefaults.AuthenticationScheme).Returns(options);
        validator = new LogoutTokenValidator(monitor, replay, clock);
    }

    [Fact]
    public async Task AValidTokenIsAcceptedWithItsSidAndItsJtiClaimedForTheReplayWindow()
    {
        Dictionary<string, object> claims = Claims();

        LogoutTokenResult result = await ValidateAsync(Sign(claims));

        Assert.True(result.IsAccepted);
        Assert.Equal(SessionId, result.SessionId);
        (string issuer, string tokenId, TimeSpan window) = Assert.Single(replay.Claims);
        Assert.Equal((Issuer, (string)claims["jti"], TimeSpan.FromSeconds(180)), (issuer, tokenId, window));
        // What the endpoint needs to release the claim if deleting the tickets fails.
        Assert.Equal((Issuer, (string)claims["jti"]), (result.Issuer, result.TokenId));
    }

    [Fact]
    public async Task SubIsOptionalAndAnAudienceArrayContainingTheClientIsAccepted()
    {
        Dictionary<string, object> claims = Claims();
        claims.Remove("sub");
        claims["aud"] = new[] { "another-client", ClientId };

        Assert.True((await ValidateAsync(Sign(claims))).IsAccepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.!!!.sig")]
    public async Task AMissingOrUnreadableTokenIsMalformed(string? token) =>
        await AssertRefusedBeforeRedisAsync(token, LogoutTokenFailure.Malformed);

    [Fact]
    public async Task AnOversizedTokenIsMalformedBeforeItIsParsed()
    {
        Dictionary<string, object> claims = Claims();
        claims["padding"] = new string('x', LogoutTokenValidator.MaxTokenLength);

        await AssertRefusedBeforeRedisAsync(Sign(claims), LogoutTokenFailure.Malformed);
    }

    // Rule 1: the signature, with the keys of the login handler's configuration.
    [Fact]
    public async Task ATokenSignedWithAnotherKeyUnderTheSameKidIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(), new RsaSecurityKey(RSA.Create(2048)) { KeyId = signingKey.KeyId }), LogoutTokenFailure.Signature);

    [Fact]
    public async Task ATokenWithAnUnknownKidIsRefusedAndAsksForFreshKeys()
    {
        await AssertRefusedBeforeRedisAsync(Sign(Claims(), new RsaSecurityKey(RSA.Create(2048)) { KeyId = "rotated" }), LogoutTokenFailure.Signature);

        configurationManager.Received(1).RequestRefresh();
    }

    // Rule 2: alg is in the expected set, never none.
    [Fact]
    public async Task AnUnsignedAlgNoneTokenIsRefused()
    {
        string header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"logout+jwt"}""");
        string payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(Claims()));

        await AssertRefusedBeforeRedisAsync(header + "." + payload + ".", LogoutTokenFailure.Algorithm);
    }

    [Fact]
    public async Task AnHmacTokenIsRefusedEvenWhenItsSignatureIsSound()
    {
        SymmetricSecurityKey hmac = new(RandomNumberGenerator.GetBytes(32)) { KeyId = signingKey.KeyId };

        await AssertRefusedBeforeRedisAsync(Sign(Claims(), hmac, algorithm: SecurityAlgorithms.HmacSha256), LogoutTokenFailure.Algorithm);
    }

    // Explicit typing: an ID token (typ JWT) signed by the same key must not pass as a logout token.
    [Theory]
    [InlineData("JWT")]
    [InlineData("at+jwt")]
    public async Task ATokenOfAnotherTypeIsRefused(string type) =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(), type: type), LogoutTokenFailure.Type);

    // Rule 3: iss is the configured issuer.
    [Theory]
    [InlineData("https://idp.test/realms/master")]
    [InlineData("http://keycloak:8080/realms/dispute-portal")]
    public async Task AWrongIssuerIsRefused(string issuer) =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("iss", issuer))), LogoutTokenFailure.Issuer);

    // Rule 3: aud contains our client id.
    [Fact]
    public async Task AWrongAudienceIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("aud", "account"))), LogoutTokenFailure.Audience);

    // Rule 3: exp is honoured when present.
    [Fact]
    public async Task AnExpiredTokenIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("iat", Seconds(-60)), ("exp", Seconds(-31)))), LogoutTokenFailure.Expired);

    [Fact]
    public async Task ATokenWithoutExpIsAccepted()
    {
        Dictionary<string, object> claims = Claims();
        claims.Remove("exp");

        Assert.True((await ValidateAsync(Sign(claims))).IsAccepted);
    }

    // Rule 3: iat is present and recent (30 s of skew either way, at most two minutes old).
    [Theory]
    [InlineData(31)]
    [InlineData(3600)]
    [InlineData(-151)]
    [InlineData(-86400)]
    public async Task AnIatFarInTheFutureOrThePastIsRefused(int offsetSeconds) =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("iat", Seconds(offsetSeconds)), ("exp", Seconds(offsetSeconds + 120)))), LogoutTokenFailure.IssuedAt);

    [Theory]
    [InlineData(30)]
    [InlineData(-150)]
    public async Task AnIatAtTheEdgeOfTheWindowIsAccepted(int offsetSeconds) =>
        Assert.True((await ValidateAsync(Sign(Claims(("iat", Seconds(offsetSeconds)), ("exp", Seconds(offsetSeconds + 300)))))).IsAccepted);

    [Fact]
    public async Task AMissingIatIsRefused()
    {
        Dictionary<string, object> claims = Claims();
        claims.Remove("iat");

        await AssertRefusedBeforeRedisAsync(Sign(claims), LogoutTokenFailure.IssuedAt);
    }

    [Fact]
    public async Task AnIatThatIsNotANumberIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("iat", Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)))), LogoutTokenFailure.IssuedAt);

    [Fact]
    public async Task ANotBeforeInTheFutureIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("nbf", Seconds(60)))), LogoutTokenFailure.NotYetValid);

    // Rule 4: events contains the back-channel logout member, whose value is a JSON object.
    [Fact]
    public async Task AMissingEventsClaimIsRefused()
    {
        Dictionary<string, object> claims = Claims();
        claims.Remove("events");

        await AssertRefusedBeforeRedisAsync(Sign(claims), LogoutTokenFailure.Events);
    }

    [Theory]
    [InlineData("""{"http://schemas.openid.net/event/other":{}}""")]
    [InlineData("""{"http://schemas.openid.net/event/backchannel-logout":"yes"}""")]
    [InlineData("""["http://schemas.openid.net/event/backchannel-logout"]""")]
    public async Task AnEventsClaimWithoutTheLogoutEventObjectIsRefused(string events) =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("events", JsonDocument.Parse(events).RootElement))), LogoutTokenFailure.Events);

    // Rule 5: nonce must be absent (an ID token carries one).
    [Fact]
    public async Task ANoncePresentIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("nonce", "n-0S6_WzA2Mj"))), LogoutTokenFailure.Nonce);

    // Rule 6 (jti, for replay detection).
    [Fact]
    public async Task AMissingJtiIsRefused()
    {
        Dictionary<string, object> claims = Claims();
        claims.Remove("jti");

        await AssertRefusedBeforeRedisAsync(Sign(claims), LogoutTokenFailure.TokenId);
    }

    [Fact]
    public async Task AnEmptyJtiIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("jti", ""))), LogoutTokenFailure.TokenId);

    // The JWT library itself refuses a registered claim of the wrong JSON type while reading the token.
    [Fact]
    public async Task ANonStringJtiIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("jti", 42))), LogoutTokenFailure.Malformed);

    // Rule 7: sid is required (the client sets backchannel.logout.session.required).
    [Fact]
    public async Task AMissingSidIsRefused()
    {
        Dictionary<string, object> claims = Claims();
        claims.Remove("sid");

        await AssertRefusedBeforeRedisAsync(Sign(claims), LogoutTokenFailure.SessionId);
    }

    [Fact]
    public async Task AnOverlongSidIsRefused() =>
        await AssertRefusedBeforeRedisAsync(Sign(Claims(("sid", new string('s', 257)))), LogoutTokenFailure.SessionId);

    // Rule 6: a jti already seen within the window is a replay.
    [Fact]
    public async Task AReplayedJtiIsRefused()
    {
        string token = Sign(Claims());

        LogoutTokenResult first = await ValidateAsync(token);
        LogoutTokenResult second = await ValidateAsync(token);

        Assert.True(first.IsAccepted);
        Assert.Equal(LogoutTokenFailure.Replayed, second.Failure);
        Assert.Null(second.SessionId);
    }

    [Fact]
    public async Task AnUnreachableIdentityProviderIs503NotARefusal()
    {
        configurationManager.GetConfigurationAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("IDX20803"));

        await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() => ValidateAsync(Sign(Claims())));
    }

    [Fact]
    public void TheReplayWindowCoversEveryIatTheValidatorStillAccepts() =>
        Assert.True(LogoutTokenValidator.ReplayWindow >= LogoutTokenValidator.MaxAge + (2 * LogoutTokenValidator.ClockSkew));

    private Task<LogoutTokenResult> ValidateAsync(string? token) => validator.ValidateAsync(token, CancellationToken.None);

    // Stateless rules are decided before Redis: an unsigned or forged token never costs a Redis round trip.
    private async Task AssertRefusedBeforeRedisAsync(string? token, LogoutTokenFailure expected)
    {
        LogoutTokenResult result = await ValidateAsync(token);

        Assert.False(result.IsAccepted);
        Assert.Equal(expected, result.Failure);
        Assert.Null(result.SessionId);
        Assert.Empty(replay.Claims);
    }

    private static long Seconds(int offset) => Now.AddSeconds(offset).ToUnixTimeSeconds();

    private static Dictionary<string, object> Claims(params (string Name, object Value)[] overrides)
    {
        Dictionary<string, object> claims = new(StringComparer.Ordinal)
        {
            ["iss"] = Issuer,
            ["aud"] = ClientId,
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["exp"] = Now.AddMinutes(2).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString(),
            ["sub"] = "a1b2c3d4-0000-4000-8000-000000000001",
            ["sid"] = SessionId,
            ["events"] = new Dictionary<string, object> { [LogoutTokenValidator.BackchannelLogoutEvent] = new Dictionary<string, object>() },
        };
        foreach ((string name, object value) in overrides)
        {
            claims[name] = value;
        }

        return claims;
    }

    private string Sign(Dictionary<string, object> claims, SecurityKey? key = null, string type = LogoutTokenValidator.TokenType, string algorithm = SecurityAlgorithms.RsaSha256)
    {
        // The payload exactly as given: no exp, iat or nbf added behind the test's back.
        JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false };
        string token = handler.CreateToken(
            JsonSerializer.Serialize(claims),
            new SigningCredentials(key ?? signingKey, algorithm),
            new Dictionary<string, object> { ["typ"] = type });
        using JsonDocument header = JsonDocument.Parse(Base64UrlEncoder.Decode(token.Split('.')[0]));
        Assert.Equal(type, header.RootElement.GetProperty("typ").GetString());
        return token;
    }

    private sealed class RecordingReplayCache : ILogoutTokenReplayCache
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);

        public List<(string Issuer, string TokenId, TimeSpan Window)> Claims { get; } = [];

        public Task<bool> TryClaimAsync(string issuer, string tokenId, TimeSpan window)
        {
            Claims.Add((issuer, tokenId, window));
            return Task.FromResult(seen.Add(issuer + "\n" + tokenId));
        }

        public Task ReleaseAsync(string issuer, string tokenId)
        {
            seen.Remove(issuer + "\n" + tokenId);
            return Task.CompletedTask;
        }
    }
}
