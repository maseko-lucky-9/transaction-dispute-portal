using System.Net;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using StackExchange.Redis;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// Gaps the independent test review found by mutation (PR #1): each test pins one behaviour a surviving mutant could
/// change. Real cookie + OIDC handlers, the fake IdP, and the compose ACL user on Redis, as in ProductionAuthStackTests.
/// </summary>
[Collection("api")]
public sealed class AuthHardeningTests : IDisposable
{
    private const string TrustedProxy = "172.28.0.5";
    private const string UntrustedPeer = "203.0.113.9";

    private readonly ApiFactory api;

    public AuthHardeningTests(ApiFactory api)
    {
        this.api = api;
        api.IdentityProvider.Reset();
    }

    private static IdpUser Thandi() => new(Seeder.ThandiSubject, "Thandi Nkosi", [Roles.Customer]);

    private RedisTicketStore Store => api.Services.GetRequiredService<RedisTicketStore>();

    // 1. Every refresh must persist the rotated refresh token: with reuse 0, a ticket still holding the old one fails
    //    the next refresh. A 30 s access token makes both requests refresh.
    [Fact]
    public async Task EachRefreshStoresTheRotatedRefreshTokenAndTheNextRefreshRedeemsIt()
    {
        api.IdentityProvider.AccessTokenLifetimeSeconds = 30;
        (BrowserSession session, string ticketId) = await RedisTickets.SignInAsync(api, Thandi());
        int before = api.IdentityProvider.RefreshCount;
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage first = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));
        string? storedAfterFirst = await StoredRefreshTokenAsync(ticketId);
        string? issuedAfterFirst = api.IdentityProvider.LastIssuedRefreshToken;
        HttpResponseMessage second = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(before + 2, api.IdentityProvider.RefreshCount);
        Assert.Equal(issuedAfterFirst, storedAfterFirst);
        Assert.Equal(api.IdentityProvider.LastIssuedRefreshToken, await StoredRefreshTokenAsync(ticketId));
    }

    // 2. A refreshed ID token is validated exactly like the first: a forged signature ends the session and its ticket.
    [Fact]
    public async Task ARefreshedIdTokenSignedWithAnUnknownKeyEndsTheSessionAndRemovesTheTicket()
    {
        api.IdentityProvider.AccessTokenLifetimeSeconds = 30;
        (BrowserSession session, string ticketId) = await RedisTickets.SignInAsync(api, Thandi());
        api.IdentityProvider.SignRefreshedIdTokenWithUnknownKey = true;
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(ticketId, RedisTickets.Ids(api));
    }

    // 3. A cross-site link to /bff/logout carries no sid at all.
    [Fact]
    public async Task LogoutWithoutASidIs400AndKeepsTheSession()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage logout = await browser.SendAsync(session.Request(HttpMethod.Get, "/bff/logout", csrf: false));
        HttpResponseMessage after = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal("invalid_logout_request", (await Problem.ReadAsync(logout)).Code);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task AnonymousLogoutRedirectsHome()
    {
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync("/bff/logout");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    // 4. With host filtering off, the OIDC redirect URIs still come from Public:BaseUrl, never from the Host header.
    [Fact]
    public async Task RedirectUrisComeFromConfigurationNotFromTheHostHeader()
    {
        IdpUser user = Thandi();
        BrowserSession session = await BrowserSession.SignInAsync(api, user);
        await using WebApplicationFactory<Program> anyHost = api.WithWebHostBuilder(builder => builder.UseSetting("AllowedHosts", "*"));
        using HttpClient evil = anyHost.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("http://evil.example"),
        });

        HttpResponseMessage login = await evil.GetAsync("/bff/login");
        HttpResponseMessage logout = await evil.SendAsync(session.Request(HttpMethod.Get, "/bff/logout?sid=" + user.SessionId, csrf: false));

        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal("http://localhost/signin-oidc", Query(login)["redirect_uri"].ToString());
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.Equal("http://localhost/signout-callback-oidc", Query(logout)["post_logout_redirect_uri"].ToString());
    }

    [Fact]
    public async Task UnderTheDefaultConfigurationAForeignHostIs400()
    {
        using HttpClient evil = api.CreateBrowserClient("http://evil.example");

        HttpResponseMessage response = await evil.GetAsync("/bff/user");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // 5. X-Forwarded-For is honoured only from the trusted proxy network, and the limiter's 429 is a full problem.
    [Fact]
    public async Task TheRateLimitPartitionUsesTheForwardedClientOnlyBehindTheTrustedProxy()
    {
        await using WebApplicationFactory<Program> limited = api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:PermitsPerMinute", "2");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, PeerAddressFilter>());
        });
        using HttpClient browser = limited.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        Task<HttpResponseMessage> Login(string peer, string forwardedFor) => browser.SendAsync(LoginRequest(peer, forwardedFor));

        // Behind the trusted proxy: each forwarded client has its own quota.
        Assert.Equal(HttpStatusCode.Found, (await Login(TrustedProxy, "198.51.100.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await Login(TrustedProxy, "198.51.100.1")).StatusCode);
        HttpResponseMessage rejected = await Login(TrustedProxy, "198.51.100.1");
        Assert.Equal(HttpStatusCode.Found, (await Login(TrustedProxy, "198.51.100.2")).StatusCode);
        // An untrusted peer: a spoofed X-Forwarded-For buys no fresh quota.
        Assert.Equal(HttpStatusCode.Found, (await Login(UntrustedPeer, "198.51.100.77")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await Login(UntrustedPeer, "198.51.100.78")).StatusCode);
        HttpResponseMessage spoofed = await Login(UntrustedPeer, "198.51.100.79");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(RateLimits.RejectionCode, (await Problem.ReadAsync(rejected)).Code);
        Assert.Equal(HttpStatusCode.TooManyRequests, spoofed.StatusCode);
    }

    // 6. A cancelled or tampered sign-in is a 400 problem, not a 500.
    [Fact]
    public async Task ACancelledSignInIs400LoginFailed()
    {
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync("/signin-oidc?error=access_denied&state=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("login_failed", (await Problem.ReadAsync(response)).Code);
    }

    // 7. Literals on purpose: a change to the session policy must show up as a failing test, not a silent constant edit.
    [Fact]
    public async Task TheTicketSlidesFor15MinutesLivesAtMost8HoursAndHoldsOnlyThreeTokens()
    {
        (_, string ticketId) = await RedisTickets.SignInAsync(api, Thandi());
        AuthenticationTicket ticket = (await Store.RetrieveAsync(ticketId))!;
        TimeSpan? ttl = await api.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyTimeToLiveAsync(RedisTicketStore.KeyPrefix + ticketId);

        Assert.Equal(TimeSpan.FromMinutes(15), ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        Assert.InRange((SessionTokens.AbsoluteExpiresAt(ticket.Properties) - ticket.Properties.IssuedUtc!.Value).TotalMinutes, 479, 481);
        Assert.InRange(ttl!.Value.TotalMinutes, 475, 480);
        // SaveTokens stays off: no access_token or token_type is ever stored.
        Assert.Equal(["expires_at", "id_token", "refresh_token"], ticket.Properties.GetTokens().Select(token => token.Name).Order(StringComparer.Ordinal));
        Assert.Equal(TimeSpan.FromMinutes(15), SessionTokens.IdleTimeout);
        Assert.Equal(TimeSpan.FromHours(8), SessionTokens.AbsoluteLifetime);
    }

    // 9. Only the exact value 1 satisfies the CSRF header rule.
    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("")]
    [InlineData("1, 1")]
    public async Task AnyCsrfHeaderValueOtherThanExactlyOneIsRefused(string value)
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();
        using HttpRequestMessage request = session.Request(HttpMethod.Get, "/api/v1/me", csrf: false);
        request.Headers.TryAddWithoutValidation(CsrfMiddleware.HeaderName, value);

        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CsrfMiddleware.Code, (await Problem.ReadAsync(response)).Code);
    }

    public void Dispose() => api.IdentityProvider.Reset();

    private async Task<string?> StoredRefreshTokenAsync(string ticketId) =>
        (await Store.RetrieveAsync(ticketId))?.Properties.GetTokenValue(SessionTokens.RefreshToken);

    private static Dictionary<string, StringValues> Query(HttpResponseMessage redirect) =>
        QueryHelpers.ParseQuery(redirect.Headers.Location!.Query);

    private static HttpRequestMessage LoginRequest(string peer, string forwardedFor)
    {
        HttpRequestMessage request = new(HttpMethod.Get, "/bff/login");
        request.Headers.Add(PeerAddressFilter.HeaderName, peer);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    /// <summary>TestServer has no socket peer; this sets one per request, before the app's forwarded-headers middleware.</summary>
    private sealed class PeerAddressFilter : IStartupFilter
    {
        public const string HeaderName = "X-Test-Peer";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out StringValues peer))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
