using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DisputePortal.Api.Features.Bff;
using DisputePortal.Api.Shared.Health;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.OpenApi;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// OpenID Connect Back-Channel Logout (ADR-011) on the production auth stack: the real cookie + OIDC handlers sign in
/// against the fake IdP, which also signs the logout tokens Keycloak would send. Keycloak posts to the management port
/// under its compose alias (<see cref="ApiFactory.ManagementBaseUrl"/>), with no cookie and no CSRF header.
/// </summary>
[Collection("api")]
public sealed class BackchannelLogoutTests : IDisposable
{
    private const string TrustedProxy = "172.28.0.5";
    private const string KeycloakOnTheDataNetwork = ApiFactory.KeycloakPeer;

    private readonly ApiFactory api;

    public BackchannelLogoutTests(ApiFactory api)
    {
        this.api = api;
        api.IdentityProvider.Reset();
    }

    private IDatabase Redis => api.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private static IdpUser Thandi() => new(Seeder.ThandiSubject, "Thandi Nkosi", [Roles.Customer]);

    [Fact]
    public async Task AValidLogoutTokenDeletesTheSessionsTicketAndItsIndexAndTheNextRequestIs401()
    {
        IdpUser user = Thandi();
        (BrowserSession session, string ticketId) = await RedisTickets.SignInAsync(api, user);
        Assert.Equal(HttpStatusCode.OK, (await UserAsync(session)).StatusCode);
        Assert.True(await Redis.SetContainsAsync(RedisTicketStore.SessionIndexKey(user.SessionId), ticketId));

        HttpResponseMessage logout = await PostAsync(api.IdentityProvider.LogoutToken(user.SessionId, user.Subject));

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal("no-store", logout.Headers.CacheControl?.ToString());
        Assert.DoesNotContain(ticketId, RedisTickets.Ids(api));
        Assert.False(await Redis.KeyExistsAsync(RedisTicketStore.SessionIndexKey(user.SessionId)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserAsync(session)).StatusCode);
    }

    // Spec section 2.8: a valid token for a session the RP does not know (or already ended) is still a success.
    [Fact]
    public async Task AValidTokenForAnUnknownSidIs200AndEveryOtherSessionIsUnaffected()
    {
        (BrowserSession session, string ticketId) = await RedisTickets.SignInAsync(api, Thandi());

        HttpResponseMessage logout = await PostAsync(api.IdentityProvider.LogoutToken("never-signed-in-here", Seeder.ThandiSubject));

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Contains(ticketId, RedisTickets.Ids(api));
        Assert.Equal(HttpStatusCode.OK, (await UserAsync(session)).StatusCode);
    }

    // Same user, two Keycloak sessions (two devices): the sid, not the subject, picks the session that ends.
    [Fact]
    public async Task OfTwoSessionsOfOneUserOnlyTheOneWithTheMatchingSidEnds()
    {
        IdpUser laptop = Thandi();
        IdpUser phone = Thandi();
        BrowserSession onLaptop = await BrowserSession.SignInAsync(api, laptop);
        BrowserSession onPhone = await BrowserSession.SignInAsync(api, phone);
        Assert.NotEqual(laptop.SessionId, phone.SessionId);

        HttpResponseMessage logout = await PostAsync(api.IdentityProvider.LogoutToken(laptop.SessionId, laptop.Subject));

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserAsync(onLaptop)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UserAsync(onPhone)).StatusCode);
    }

    // One Keycloak session, two app tickets (a second sign-in in another tab, through Keycloak's SSO): both end.
    [Fact]
    public async Task EveryTicketSignedInUnderTheKeycloakSessionEnds()
    {
        IdpUser user = Thandi();
        (BrowserSession first, string firstTicket) = await RedisTickets.SignInAsync(api, user);
        (BrowserSession second, string secondTicket) = await RedisTickets.SignInAsync(api, user);

        await PostAsync(api.IdentityProvider.LogoutToken(user.SessionId, user.Subject));

        Assert.DoesNotContain(firstTicket, RedisTickets.Ids(api));
        Assert.DoesNotContain(secondTicket, RedisTickets.Ids(api));
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserAsync(second)).StatusCode);
    }

    [Fact]
    public async Task ANormalLogoutRemovesTheTicketFromTheSessionIndex()
    {
        IdpUser user = Thandi();
        (BrowserSession session, string ticketId) = await RedisTickets.SignInAsync(api, user);
        RedisKey index = RedisTicketStore.SessionIndexKey(user.SessionId);
        Assert.True(await Redis.SetContainsAsync(index, ticketId));
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage logout = await browser.SendAsync(session.Request(HttpMethod.Get, "/bff/logout?sid=" + user.SessionId, csrf: false));

        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.False(await Redis.SetContainsAsync(index, ticketId));
        // Its only member gone, Redis drops the set itself.
        Assert.False(await Redis.KeyExistsAsync(index));
    }

    [Fact]
    public async Task TheIndexHoldsTicketIdsUnderAHashedSidAndNoTokenMaterial()
    {
        IdpUser user = Thandi();
        (_, string ticketId) = await RedisTickets.SignInAsync(api, user);
        RedisKey index = RedisTicketStore.SessionIndexKey(user.SessionId);

        RedisValue[] members = await Redis.SetMembersAsync(index);
        TimeSpan? ttl = await Redis.KeyTimeToLiveAsync(index);

        Assert.Equal([ticketId], members.Select(member => member.ToString()));
        Assert.DoesNotContain(user.SessionId, index.ToString(), StringComparison.Ordinal);
        Assert.InRange(ttl!.Value, SessionTokens.AbsoluteLifetime - TimeSpan.FromMinutes(1), SessionTokens.AbsoluteLifetime);
    }

    public static TheoryData<string> RefusedTokens() =>
    [
        "garbage",
        "bad-signature",
        "wrong-audience",
        "nonce",
        "missing-sid",
        "alg-none",
    ];

    // Every refusal looks the same from outside (spec section 2.8): one status, one code, one detail. No oracle.
    [Theory]
    [MemberData(nameof(RefusedTokens))]
    public async Task ARefusedTokenIs400WithTheSameGenericBodyAndTheSessionSurvives(string kind)
    {
        IdpUser user = Thandi();
        (BrowserSession session, string ticketId) = await RedisTickets.SignInAsync(api, user);

        HttpResponseMessage response = await PostAsync(Refused(kind, user));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Problem problem = await Problem.ReadAsync(response);
        Assert.Equal(BackchannelLogoutEndpoint.RefusedCode, problem.Code);
        Assert.Equal(BackchannelLogoutEndpoint.RefusedDetail, problem.Detail);
        Assert.Contains(ticketId, RedisTickets.Ids(api));
        Assert.Equal(HttpStatusCode.OK, (await UserAsync(session)).StatusCode);
    }

    [Fact]
    public async Task AReplayedTokenIs400TheSecondTime()
    {
        IdpUser user = Thandi();
        await RedisTickets.SignInAsync(api, user);
        string token = api.IdentityProvider.LogoutToken(user.SessionId, user.Subject);

        HttpResponseMessage first = await PostAsync(token);
        HttpResponseMessage replay = await PostAsync(token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(BackchannelLogoutEndpoint.RefusedCode, (await Problem.ReadAsync(replay)).Code);
    }

    // The replay claim lives in Redis, so a second replica sees the jti the first one claimed.
    [Fact]
    public async Task AReplayToAnotherReplicaIsRefusedToo()
    {
        IdpUser user = Thandi();
        string token = api.IdentityProvider.LogoutToken(user.SessionId, user.Subject);
        await using WebApplicationFactory<Program> replicaB = api.WithWebHostBuilder(_ => { });

        HttpResponseMessage onA = await PostAsync(token);
        HttpResponseMessage onB = await PostAsync(token, replicaB);

        Assert.Equal(HttpStatusCode.OK, onA.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, onB.StatusCode);
    }

    [Theory]
    [InlineData("missing-content-type")]
    [InlineData("json")]
    [InlineData("multipart")]
    [InlineData("two-tokens")]
    [InlineData("no-token-field")]
    [InlineData("oversized")]
    public async Task AnythingButOneLogoutTokenInASmallUrlEncodedFormIs400(string kind)
    {
        IdpUser user = Thandi();
        (BrowserSession session, _) = await RedisTickets.SignInAsync(api, user);
        string token = api.IdentityProvider.LogoutToken(user.SessionId, user.Subject);
        using HttpClient keycloak = api.CreateBrowserClient(ApiFactory.ManagementBaseUrl);

        HttpResponseMessage response = await keycloak.PostAsync(BackchannelLogoutEndpoint.Path, Body(kind, token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(BackchannelLogoutEndpoint.RefusedCode, (await Problem.ReadAsync(response)).Code);
        Assert.Equal(HttpStatusCode.OK, (await UserAsync(session)).StatusCode);
    }

    // Reachability: never on the public port (what nginx proxies to), whatever the Host header or the path's spelling.
    [Theory]
    [InlineData("http://localhost", BackchannelLogoutEndpoint.Path)]
    [InlineData("http://localhost:8080", BackchannelLogoutEndpoint.Path)]
    [InlineData("http://localhost", "/BFF/Backchannel-Logout/")]
    [InlineData("http://" + ApiFactory.InternalHost, BackchannelLogoutEndpoint.Path)]
    public async Task TheEndpointIsNotServedOnThePublicPort(string baseUrl, string path)
    {
        IdpUser user = Thandi();
        (BrowserSession session, string ticketId) = await RedisTickets.SignInAsync(api, user);
        using HttpClient client = api.CreateBrowserClient(baseUrl);

        HttpResponseMessage response = await client.PostAsync(path, Form(api.IdentityProvider.LogoutToken(user.SessionId, user.Subject)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(ticketId, RedisTickets.Ids(api));
        Assert.Equal(HttpStatusCode.OK, (await UserAsync(session)).StatusCode);
    }

    // The internal name is admitted by host filtering only for the management port.
    [Theory]
    [InlineData("/bff/user")]
    [InlineData("/api/v1/me")]
    public async Task TheInternalHostIsRefusedOnThePublicPort(string path)
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient client = api.CreateBrowserClient("http://" + ApiFactory.InternalHost);

        HttpResponseMessage response = await client.SendAsync(session.Request(HttpMethod.Get, path));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The management port still serves nothing of the app, CSRF exemption or not.
    [Fact]
    public async Task TheManagementPortServesNoOtherBffRoute()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient keycloak = api.CreateBrowserClient(ApiFactory.ManagementBaseUrl);

        HttpResponseMessage user = await keycloak.SendAsync(session.Request(HttpMethod.Get, "/bff/user", csrf: false));
        HttpResponseMessage logout = await keycloak.SendAsync(session.Request(HttpMethod.Get, "/bff/logout", csrf: false));

        Assert.Equal(HttpStatusCode.NotFound, user.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, logout.StatusCode);
    }

    [Fact]
    public async Task WhenRedisIsDownTheEndpointFailsClosedWith503()
    {
        IdpUser user = Thandi();
        await RedisTickets.SignInAsync(api, user);
        string token = api.IdentityProvider.LogoutToken(user.SessionId, user.Subject);

        await api.Redis.PauseAsync();
        HttpResponseMessage response;
        try
        {
            response = await PostAsync(token);
        }
        finally
        {
            await api.Redis.UnpauseAsync();
            await WaitForRedisAsync();
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("session_store_unavailable", (await Problem.ReadAsync(response)).Code);
    }

    // The limit is set low here on purpose; the default is high (see the next test).
    [Fact]
    public async Task TheEndpointIsRateLimitedPerPeer()
    {
        await using WebApplicationFactory<Program> limited = api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:BackchannelLogoutsPerMinute", "2");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, PeerAddressFilter>());
        });

        HttpStatusCode[] fromKeycloak = [.. await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PostFromAsync(limited, KeycloakOnTheDataNetwork)))];
        HttpStatusCode otherPeer = await PostFromAsync(limited, "10.231.28.21");

        Assert.Equal(2, fromKeycloak.Count(status => status == HttpStatusCode.BadRequest));
        Assert.Equal(1, fromKeycloak.Count(status => status == HttpStatusCode.TooManyRequests));
        Assert.Equal(HttpStatusCode.BadRequest, otherPeer);
    }

    // Every Keycloak logout comes from one peer, so a low default would let a user who loops sign-in and logout starve the
    // real calls. The default (6000 a minute) lets a burst of a hundred through untouched.
    [Fact]
    public async Task TheDefaultLimitLetsAMassLogoutThrough()
    {
        await using WebApplicationFactory<Program> replica = api.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, PeerAddressFilter>()));
        List<HttpStatusCode> statuses = [];

        for (int request = 0; request < 100; request++)
        {
            statuses.Add(await PostFromAsync(replica, KeycloakOnTheDataNetwork));
        }

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.BadRequest, status));
    }

    // Fix for a lost claim: the jti is claimed before the tickets are deleted. When the deletion fails (Redis refuses the
    // script here, so the claim and its release still work), the claim is released and Keycloak's retry goes through.
    [Fact]
    public async Task AFailedDeletionReleasesTheJtiSoARetryOfTheSameTokenSucceeds()
    {
        IdpUser user = Thandi();
        (BrowserSession session, string ticketId) = await RedisTickets.SignInAsync(api, user);
        string token = api.IdentityProvider.LogoutToken(user.SessionId, user.Subject);

        HttpResponseMessage failed;
        await api.SetRedisCommandsAsync(allowed: false, "eval", "evalsha");
        try
        {
            failed = await PostAsync(token);
        }
        finally
        {
            await api.SetRedisCommandsAsync(allowed: true, "eval", "evalsha");
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal("session_store_unavailable", (await Problem.ReadAsync(failed)).Code);
        Assert.Contains(ticketId, RedisTickets.Ids(api));
        HttpResponseMessage retry = await PostAsync(token);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.DoesNotContain(ticketId, RedisTickets.Ids(api));
        Assert.Equal(HttpStatusCode.Unauthorized, (await UserAsync(session)).StatusCode);
    }

    // The release is best effort: when it fails too, the caller still gets the original 503, never a 500. Both delete
    // commands are taken away: StackExchange.Redis 3 deletes with UNLINK (denying DEL alone left the release working).
    [Fact]
    public async Task AFailedReleaseStillAnswersTheOriginal503()
    {
        IdpUser user = Thandi();
        await RedisTickets.SignInAsync(api, user);
        string token = api.IdentityProvider.LogoutToken(user.SessionId, user.Subject);

        HttpResponseMessage failed;
        await api.SetRedisCommandsAsync(allowed: false, "eval", "evalsha", "del", "unlink");
        try
        {
            failed = await PostAsync(token);
        }
        finally
        {
            await api.SetRedisCommandsAsync(allowed: true, "eval", "evalsha", "del", "unlink");
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal("session_store_unavailable", (await Problem.ReadAsync(failed)).Code);
    }

    // Misconfiguration fails start-up: an internal host name without the network it may be used from.
    [Fact]
    public async Task AnInternalHostWithoutAnInternalNetworkStopsStartUp()
    {
        WebApplicationFactory<Program> misconfigured = api.WithWebHostBuilder(builder => builder.UseSetting("Ports:InternalNetwork", ""));

        Exception error = Assert.ThrowsAny<Exception>(() => misconfigured.CreateClient());

        Assert.Contains(ManagementSettingsValidator.MissingNetwork, Messages(error));
        try
        {
            await misconfigured.DisposeAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or AggregateException or OptionsValidationException)
        {
            // A host that never started may refuse a clean stop; only the start-up failure matters here.
        }
    }

    // On the management port the socket's peer counts, never X-Forwarded-For: a container on the trusted app network
    // (where nginx is) cannot pass for Keycloak on the data network.
    [Fact]
    public async Task OnlyPeersOnTheDataNetworkReachTheEndpointAndForwardedForIsIgnoredThere()
    {
        await using WebApplicationFactory<Program> compose = api.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, PeerAddressFilter>()));

        HttpStatusCode keycloak = await PostFromAsync(compose, KeycloakOnTheDataNetwork);
        HttpStatusCode appNetwork = await PostFromAsync(compose, TrustedProxy);
        HttpStatusCode spoofed = await PostFromAsync(compose, TrustedProxy, forwardedFor: KeycloakOnTheDataNetwork);

        Assert.Equal(HttpStatusCode.BadRequest, keycloak);
        Assert.Equal(HttpStatusCode.NotFound, appNetwork);
        Assert.Equal(HttpStatusCode.NotFound, spoofed);
    }

    // Fixed messages; no sid, sub, jti or token in any log line, accepted or refused.
    [Fact]
    public async Task TheLogsCarryAReasonOrACountButNoTokenSidSubOrJti()
    {
        RecordingLoggerProvider logs = new();
        await using WebApplicationFactory<Program> host = api.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
        IdpUser user = Thandi();
        await RedisTickets.SignInAsync(api, user);
        const string Jti = "jti-7f1c2d9e-secret";
        string accepted = api.IdentityProvider.LogoutToken(user.SessionId, user.Subject, claims => claims["jti"] = Jti);
        string refused = api.IdentityProvider.LogoutToken(user.SessionId, user.Subject, claims => claims["nonce"] = "n-1");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(accepted, host)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(refused, host)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(accepted, host)).StatusCode);

        List<string> messages = [.. logs.Entries.Select(entry => entry.Message)];
        Assert.All(messages, message =>
        {
            Assert.DoesNotContain(user.SessionId, message, StringComparison.Ordinal);
            Assert.DoesNotContain(user.Subject, message, StringComparison.Ordinal);
            Assert.DoesNotContain(Jti, message, StringComparison.Ordinal);
            Assert.DoesNotContain("eyJ", message, StringComparison.Ordinal);
        });
        Assert.Contains("Back-channel logout ended 1 session(s)", messages);
        Assert.Contains("Back-channel logout token refused: Nonce", messages);
        Assert.Contains("Back-channel logout token refused: Replayed", messages);
    }

    [Fact]
    public async Task AcceptedAndRefusedTokensAreCountedAsBoundedSecurityEvents()
    {
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(api, SecurityMetrics.MeterName, "security.events");
        IdpUser user = Thandi();
        await RedisTickets.SignInAsync(api, user);

        await PostAsync(api.IdentityProvider.LogoutToken(user.SessionId, user.Subject));
        await PostAsync("garbage");

        // Literals on purpose: the runbook's queries key on them. The type is the only tag, so the series stay bounded.
        Assert.Equal(["backchannel_logout", "backchannel_logout_rejected"], probe.Samples.Select(sample => (string?)sample.Tags["type"]));
        Assert.All(probe.Samples, sample => Assert.Equal(["type"], sample.Tags.Keys));
    }

    // Keycloak's contract, not the SPA's: kept out of docs/openapi.json (the snapshot test pins the rest).
    [Fact]
    public async Task TheEndpointIsNotInTheOpenApiDocument()
    {
        using HttpClient client = api.CreateClient();

        string document = await client.GetStringAsync(ApiDocumentation.DocumentPath);

        Assert.DoesNotContain("backchannel", document, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => api.IdentityProvider.Reset();

    private async Task<HttpResponseMessage> UserAsync(BrowserSession session)
    {
        using HttpClient browser = api.CreateBrowserClient();
        return await browser.SendAsync(session.Request(HttpMethod.Get, "/bff/user"));
    }

    private async Task<HttpResponseMessage> PostAsync(string token, WebApplicationFactory<Program>? host = null)
    {
        using HttpClient keycloak = (host ?? api).CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri(ApiFactory.ManagementBaseUrl),
        });
        return await keycloak.PostAsync(BackchannelLogoutEndpoint.Path, Form(token));
    }

    private static async Task<HttpStatusCode> PostFromAsync(WebApplicationFactory<Program> host, string peer, string? forwardedFor = null)
    {
        using HttpClient keycloak = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri(ApiFactory.ManagementBaseUrl),
        });
        using HttpRequestMessage request = new(HttpMethod.Post, BackchannelLogoutEndpoint.Path) { Content = Form("garbage") };
        request.Headers.Add(PeerAddressFilter.HeaderName, peer);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return (await keycloak.SendAsync(request)).StatusCode;
    }

    private static string Messages(Exception error) =>
        error is AggregateException aggregate
            ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(Messages))
            : error.Message + (error.InnerException is null ? "" : " | " + Messages(error.InnerException));

    private static FormUrlEncodedContent Form(string token) => new([new KeyValuePair<string, string>("logout_token", token)]);

    private string Refused(string kind, IdpUser user) => kind switch
    {
        "garbage" => "not.a.jwt",
        "bad-signature" => api.IdentityProvider.LogoutToken(user.SessionId, user.Subject, key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "fake-idp" }),
        "wrong-audience" => api.IdentityProvider.LogoutToken(user.SessionId, user.Subject, claims => claims["aud"] = "account"),
        "nonce" => api.IdentityProvider.LogoutToken(user.SessionId, user.Subject, claims => claims["nonce"] = "n-1"),
        "missing-sid" => api.IdentityProvider.LogoutToken(user.SessionId, user.Subject, claims => claims.Remove("sid")),
        "alg-none" => Base64UrlEncoder.Encode("""{"alg":"none","typ":"logout+jwt"}""") + "."
            + Base64UrlEncoder.Encode(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["iss"] = FakeIdentityProvider.Issuer,
                ["aud"] = FakeIdentityProvider.ClientId,
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["jti"] = Guid.NewGuid().ToString(),
                ["sid"] = user.SessionId,
                ["events"] = new Dictionary<string, object> { [LogoutTokenValidator.BackchannelLogoutEvent] = new Dictionary<string, object>() },
            })) + ".",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static HttpContent Body(string kind, string token)
    {
        switch (kind)
        {
            case "missing-content-type":
                ByteArrayContent raw = new(Encoding.ASCII.GetBytes("logout_token=" + token));
                raw.Headers.ContentType = null;
                return raw;
            case "json":
                return new StringContent(JsonSerializer.Serialize(new { logout_token = token }), Encoding.UTF8, "application/json");
            case "multipart":
                return new MultipartFormDataContent { { new StringContent(token), "logout_token" } };
            case "two-tokens":
                return new FormUrlEncodedContent([new("logout_token", token), new("logout_token", token)]);
            case "no-token-field":
                return new FormUrlEncodedContent([new("token", token)]);
            case "oversized":
                // A valid token, and fields each within the form limits: only the declared-length bound refuses it.
                string pad = new('x', (int)(BackchannelLogoutEndpoint.MaxBodyBytes / 2) + 1);
                StringContent padded = new("logout_token=" + token + "&pad1=" + pad + "&pad2=" + pad, Encoding.ASCII);
                padded.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                return padded;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private async Task WaitForRedisAsync()
    {
        IConnectionMultiplexer redis = api.Services.GetRequiredService<IConnectionMultiplexer>();
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                await redis.GetDatabase().PingAsync();
                return;
            }
            catch (Exception exception) when (exception is RedisException or RedisTimeoutException)
            {
                await Task.Delay(200);
            }
        }
    }
}
