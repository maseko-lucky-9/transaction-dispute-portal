using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Features.Bff;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using StackExchange.Redis;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The production cookie + OIDC registration end to end: the real handlers, events, ticket store and middleware. Only
/// Keycloak is replaced, by an in-memory token endpoint (no test authentication handler anywhere).
/// </summary>
[Collection("api")]
public sealed class ProductionAuthStackTests : IDisposable
{
    private readonly ApiFactory api;

    public ProductionAuthStackTests(ApiFactory api)
    {
        this.api = api;
        api.IdentityProvider.Reset();
    }

    private static IdpUser Thandi() => new(Seeder.ThandiSubject, "Thandi Nkosi", [Roles.Customer]);

    private static IdpUser Agent() => new(Seeder.AgentSubject, "Agent Smith", [Roles.DisputeAgent]);

    // (a)
    [Fact]
    public async Task AnApiCallWithoutASessionIsA401ProblemNotARedirect()
    {
        using HttpClient browser = api.CreateBrowserClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add(CsrfMiddleware.HeaderName, "1");

        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("unauthorized", (await Problem.ReadAsync(response)).Code);
    }

    [Fact]
    public async Task TheUserEndpointWithoutASessionIsA401Problem()
    {
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync("/bff/user");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("unauthorized", (await Problem.ReadAsync(response)).Code);
    }

    // (b)
    [Theory]
    [InlineData("POST", "/api/v1/me")]
    [InlineData("GET", "/api/v1/me")]
    [InlineData("POST", "/bff/user")]
    public async Task ARequestWithoutTheCsrfHeaderIs403CsrfRequired(string method, string path)
    {
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CsrfMiddleware.Code, (await Problem.ReadAsync(response)).Code);
    }

    [Fact]
    public async Task ASignedInRequestWithoutTheCsrfHeaderIsStillRefused()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me", csrf: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CsrfMiddleware.Code, (await Problem.ReadAsync(response)).Code);
    }

    // (c)
    [Fact]
    public async Task SignInIssuesAnHttpOnlySecureLaxSessionCookie()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi(), "/disputes");

        SetCookieHeaderValue cookie = session.SessionSetCookie;
        Assert.Equal(BffAuthentication.CookieName, cookie.Name.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
        // A browser-session cookie: the server enforces the 15-minute idle and 8-hour absolute limits.
        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);
    }

    [Fact]
    public async Task TheAuthorizeRequestUsesTheCodeFlowWithPkceS256AndOnlyOpenidProfile()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());

        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(session.AuthorizeRequest.Query);
        Assert.Equal(api.IdentityProvider.Configuration.AuthorizationEndpoint, session.AuthorizeRequest.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Equal("openid profile", query["scope"].ToString());
        Assert.Equal(FakeIdentityProvider.ClientId, query["client_id"].ToString());
        Assert.Equal(ApiFactory.PublicBaseUrl + "/signin-oidc", query["redirect_uri"].ToString());
    }

    [Fact]
    public async Task TheSessionTicketInRedisIsEncryptedAndExpiresWithTheAbsoluteLifetime()
    {
        IdpUser user = Thandi();
        await BrowserSession.SignInAsync(api, user);
        IConnectionMultiplexer redis = api.Services.GetRequiredService<IConnectionMultiplexer>();
        IServer server = redis.GetServer(redis.GetEndPoints().Single());

        RedisKey[] keys = [.. server.Keys(pattern: RedisTicketStore.KeyPrefix + "*")];
        Assert.NotEmpty(keys);
        foreach (RedisKey key in keys)
        {
            byte[] payload = (byte[])(await redis.GetDatabase().StringGetAsync(key))!;
            string text = System.Text.Encoding.UTF8.GetString(payload);
            Assert.DoesNotContain(user.Subject, text, StringComparison.Ordinal);
            Assert.DoesNotContain("eyJ", text, StringComparison.Ordinal);
        }

        TimeSpan? ttl = await redis.GetDatabase().KeyTimeToLiveAsync(keys[^1]);
        Assert.InRange(ttl!.Value, SessionTokens.AbsoluteLifetime - TimeSpan.FromMinutes(5), SessionTokens.AbsoluteLifetime);
    }

    [Fact]
    public async Task ACustomerSessionMapsToItsSeededCustomer()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Seeder.ThandiCustomerId, me.GetProperty("customerId").GetGuid());
        Assert.Equal(Seeder.ThandiSubject, me.GetProperty("subject").GetString());
        Assert.Equal([Roles.Customer], me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task AnAgentHasNoCustomerRecord()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Agent());
        using HttpClient browser = api.CreateBrowserClient();

        JsonElement me = await (await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, me.GetProperty("customerId").ValueKind);
        Assert.Equal([Roles.DisputeAgent], me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task ASessionHoldingBothRolesIs403RoleConflict()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, new IdpUser(Seeder.SiphoSubject, "Sipho Dlamini", [Roles.Customer, Roles.DisputeAgent]));
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SingleRoleRequirement.Code, (await Problem.ReadAsync(response)).Code);
    }

    // (d)
    [Fact]
    public async Task ApiAndBffResponsesAreNoStore()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage ok = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));
        HttpResponseMessage user = await browser.SendAsync(session.Request(HttpMethod.Get, "/bff/user"));
        HttpResponseMessage anonymous = await browser.GetAsync("/bff/user");
        HttpResponseMessage csrf = await browser.GetAsync("/api/v1/me");

        Assert.All([ok, user, anonymous, csrf], response => Assert.Equal("no-store", response.Headers.CacheControl?.ToString()));
    }

    [Fact]
    public async Task TheUserEndpointReturnsNameRolesAndASessionBoundLogoutUrl()
    {
        IdpUser user = Thandi();
        BrowserSession session = await BrowserSession.SignInAsync(api, user);
        using HttpClient browser = api.CreateBrowserClient();

        JsonElement body = await (await browser.SendAsync(session.Request(HttpMethod.Get, "/bff/user", csrf: false))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Thandi Nkosi", body.GetProperty("name").GetString());
        Assert.Equal([Roles.Customer], body.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.Equal("/bff/logout?sid=" + user.SessionId, body.GetProperty("logoutUrl").GetString());
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("disputes")]
    public async Task LoginRefusesAReturnUrlThatIsNotALocalPath(string returnUrl)
    {
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.GetAsync("/bff/login?returnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await Problem.ReadAsync(response)).Code);
    }

    [Fact]
    public async Task LogoutWithAnotherSessionsSidIs400AndKeepsTheSession()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage logout = await browser.SendAsync(session.Request(HttpMethod.Get, "/bff/logout?sid=forged", csrf: false));
        HttpResponseMessage after = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal("invalid_logout_request", (await Problem.ReadAsync(logout)).Code);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task LogoutDeletesTheTicketAndRedirectsToKeycloakEndSessionWithIdTokenHint()
    {
        IdpUser user = Thandi();
        BrowserSession session = await BrowserSession.SignInAsync(api, user);
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage logout = await browser.SendAsync(session.Request(HttpMethod.Get, "/bff/logout?sid=" + user.SessionId, csrf: false));
        HttpResponseMessage after = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));

        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Uri endSession = logout.Headers.Location!;
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(endSession.Query);
        Assert.Equal(api.IdentityProvider.Configuration.EndSessionEndpoint, endSession.GetLeftPart(UriPartial.Path));
        Assert.False(string.IsNullOrEmpty(query["id_token_hint"].ToString()));
        Assert.Equal(ApiFactory.PublicBaseUrl + "/signout-callback-oidc", query["post_logout_redirect_uri"].ToString());
        Assert.Contains(BrowserSession.SetCookies(logout), cookie => cookie.Name == BffAuthentication.CookieName && cookie.Expires < DateTimeOffset.UtcNow);
        // The old cookie no longer opens anything: its ticket is gone from Redis.
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task ANearlyExpiredAccessTokenIsRefreshedAndTheRolesAreReRead()
    {
        api.IdentityProvider.AccessTokenLifetimeSeconds = 30;
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        api.IdentityProvider.AccessTokenLifetimeSeconds = 300;
        api.IdentityProvider.RolesOnRefresh = [Roles.DisputeAgent];
        int refreshesBefore = api.IdentityProvider.RefreshCount;
        using HttpClient browser = api.CreateBrowserClient();

        JsonElement me = await (await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"))).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement again = await (await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal([Roles.DisputeAgent], me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        Assert.Equal([Roles.DisputeAgent], again.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
        // The renewed ticket holds the new expiry, so the second request does not refresh again.
        Assert.Equal(refreshesBefore + 1, api.IdentityProvider.RefreshCount);
    }

    [Fact]
    public async Task ARefusedRefreshEndsTheSession()
    {
        api.IdentityProvider.AccessTokenLifetimeSeconds = 30;
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        api.IdentityProvider.RefuseRefresh = true;
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage refused = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));
        api.IdentityProvider.RefuseRefresh = false;
        HttpResponseMessage after = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task WhenRedisIsDownLoginAndSessionValidationFailClosedWith503()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();

        await api.Redis.PauseAsync();
        HttpResponseMessage login;
        HttpResponseMessage me;
        try
        {
            login = await browser.GetAsync("/bff/login");
            me = await browser.SendAsync(session.Request(HttpMethod.Get, "/api/v1/me"));
        }
        finally
        {
            await api.Redis.UnpauseAsync();
            await WaitForRedisAsync();
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
        Assert.Equal("session_store_unavailable", (await Problem.ReadAsync(login)).Code);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, me.StatusCode);
        Assert.Equal("session_store_unavailable", (await Problem.ReadAsync(me)).Code);
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
            catch (RedisException)
            {
                await Task.Delay(200);
            }
            catch (RedisTimeoutException)
            {
                await Task.Delay(200);
            }
        }
    }

    public void Dispose() => api.IdentityProvider.Reset();
}
