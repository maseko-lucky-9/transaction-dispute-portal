using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Features.Bff;
using DisputePortal.Api.Shared.Health;
using DisputePortal.Api.Shared.OpenApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// What every mapped route does when Postgres is gone (<see cref="ApiFactory.WithUnreachableDatabase"/>). A route that reads
/// the database answers 503 database_unavailable with Retry-After; a route that does not keeps answering normally, and the
/// table says why. The second test fails when a route is mapped but missing from the table, so a new endpoint cannot ship
/// without a stated outage behaviour.
/// </summary>
[Collection("api")]
public sealed class EndpointOutageTests(ApiFactory api)
{
    private const string DatabaseDown = "database_unavailable";

    // The method of an endpoint mapped with no HTTP method restriction (the health checks): any method reaches it.
    private const string AnyMethod = "*";

    // Nothing is stored under it, and with the database down nothing could be looked up anyway.
    private static readonly Guid SomeId = Guid.Parse("5b0c3f06-6c1e-4a53-9d1e-0d4c6f1a7e21");
    private static readonly string SomeETag = "\"1\"";

    // Routes are as the endpoint data source reports them (RoutePattern.RawText), so a group's "/" keeps its trailing slash.
    // A request is built only after its session exists, which needs the healthy fixture host.
    private static readonly OutageRoute[] Table =
    [
        // Customer routes: the Customer policy admits the session without the database, then CustomerScopeFilter maps the
        // subject to its customer row. That read is the first to fail, so each of these is refused before its own query runs.
        Customer("GET", "/api/v1/me", session => session.Request(HttpMethod.Get, "/api/v1/me"),
            "Reads the customer row for the subject."),
        Customer("GET", "/api/v1/transactions/", session => session.Request(HttpMethod.Get, "/api/v1/transactions"),
            "Customer scope lookup, then the transaction page."),
        Customer("GET", "/api/v1/transactions/{id:guid}", session => session.Request(HttpMethod.Get, "/api/v1/transactions/" + SomeId),
            "Customer scope lookup, then the transaction."),
        Customer("GET", "/api/v1/dispute-reasons/", session => session.Request(HttpMethod.Get, DisputeRequests.Reasons),
            "The reasons are rows in dispute_reasons, not a constant."),
        Customer("GET", "/api/v1/disputes/", session => session.Request(HttpMethod.Get, DisputeRequests.Disputes),
            "Customer scope lookup, then the dispute page."),
        Customer("GET", "/api/v1/disputes/{id:guid}", session => session.Request(HttpMethod.Get, DisputeRequests.Disputes + "/" + SomeId),
            "Customer scope lookup, then the dispute."),
        Customer("POST", "/api/v1/disputes/", session => DisputeRequests.Submit(session, SomeId, Guid.NewGuid()),
            "A body that binds (JSON binding runs before any filter, and an unknown field is a 400 first); the scope lookup then fails before validation runs."),
        Customer("POST", "/api/v1/disputes/{id:guid}/withdraw", session => DisputeRequests.Withdraw(session, SomeId, SomeETag),
            "No body to bind; the scope lookup fails before the command looks at If-Match."),

        // Agent routes: the DisputeAgent policy is roles from the session, no database; the queue and the dispute are Postgres
        // reads. (Their audit is a log line and a metric, not a table, so it is not what fails.)
        Agent("GET", "/api/v1/agent/disputes/", session => session.Request(HttpMethod.Get, AgentRequests.Queue),
            "The work queue is one query over disputes."),
        Agent("GET", "/api/v1/agent/disputes/{id:guid}", session => session.Request(HttpMethod.Get, AgentRequests.Detail(SomeId)),
            "One query for the dispute with its customer and history; the audit log line follows a successful read only."),
        Agent("POST", "/api/v1/agent/disputes/{id:guid}/transitions", session => AgentRequests.Transition(session, SomeId, SomeETag, "UnderReview"),
            "A body that binds; the dispute is read first (404 is decided before If-Match), so the outage is hit before any precondition."),

        // Routes that never touch Postgres, so the outage does not reach them.
        new OutageRoute("GET", "/bff/user", CustomerRequestAsync(session => session.Request(HttpMethod.Get, "/bff/user")), HttpStatusCode.OK, null,
            "Answers from the session cookie's claims; the ticket is in Redis.",
            async response => Assert.Equal("customer", Assert.Single((await JsonAsync(response)).GetProperty("roles").EnumerateArray()).GetString())),
        new OutageRoute("GET", "/bff/login", _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "/bff/login")), HttpStatusCode.Found, null,
            "Checks Redis and the cached identity-provider metadata, then redirects to Keycloak.",
            response =>
            {
                Assert.StartsWith(FakeIdentityProvider.Issuer + "/protocol/openid-connect/auth?", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
                return Task.CompletedTask;
            }),
        new OutageRoute("GET", "/bff/logout", LogoutRequestAsync, HttpStatusCode.Found, null,
            "Deletes the ticket in Redis, then redirects to Keycloak's end-session endpoint.",
            response =>
            {
                Assert.StartsWith(FakeIdentityProvider.Issuer + "/protocol/openid-connect/logout?", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
                return Task.CompletedTask;
            }),
        new OutageRoute("POST", BackchannelLogoutEndpoint.Path, BackchannelLogoutRequestAsync, HttpStatusCode.OK, null,
            "Token validation, the jti replay check and the delete-by-sid are all Redis: no Postgres call, so a Keycloak logout still lands while the database is down."),
        new OutageRoute("GET", "/openapi/{documentName}.json", _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, ApiDocumentation.DocumentPath)), HttpStatusCode.OK, null,
            "Generated from the endpoint metadata in memory. Mapped here because the test host is not Production."),

        // Health checks, on the management port: no HTTP method restriction, and a plain-text body, not Problem Details.
        new OutageRoute(AnyMethod, ManagementSettings.LivePath, _ => Task.FromResult(Management(ManagementSettings.LivePath)), HttpStatusCode.OK, null,
            "Liveness runs no checks: the process is up, so an orchestrator must not restart it for a database outage.",
            async response => Assert.Equal("Healthy", await response.Content.ReadAsStringAsync())),
        new OutageRoute(AnyMethod, ManagementSettings.ReadyPath, _ => Task.FromResult(Management(ManagementSettings.ReadyPath)), HttpStatusCode.ServiceUnavailable, null,
            "Readiness pings Postgres, so it reports the outage (and a load balancer takes the replica out).",
            async response => Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync())),
    ];

    /// <summary>One row per route: the method and the route pattern, so a failure names the route.</summary>
    public static TheoryData<string, string> Routes()
    {
        TheoryData<string, string> routes = new();
        foreach (OutageRoute route in Table)
        {
            routes.Add(route.Method, route.Pattern);
        }

        return routes;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EachRouteAnswersAsDocumentedWhenTheDatabaseIsUnreachable(string method, string pattern)
    {
        OutageRoute route = Table.Single(candidate => candidate.Method == method && candidate.Pattern == pattern);
        using HttpRequestMessage request = await route.Request(api);
        AssertTheRequestIsForTheRow(route, request);
        await using WebApplicationFactory<Program> offline = api.WithUnreachableDatabase();

        using HttpResponseMessage response = await AgentApi.SendAsync(offline, request);

        Assert.True(route.Status == response.StatusCode, $"{method} {pattern}: expected {(int)route.Status} ({route.Why}), got {(int)response.StatusCode}.");
        if (route.Code is not null)
        {
            Assert.Equal(route.Code, (await Problem.ReadAsync(response)).Code);
            Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        }

        if (route.Verify is not null)
        {
            await route.Verify(response);
        }
    }

    // The enumeration test compares the table's patterns with the mapped ones; this ties each row's request to its own pattern,
    // so a row cannot name one route and send another (any customer route answers 503, so a copy-paste slip would stay green).
    private static void AssertTheRequestIsForTheRow(OutageRoute route, HttpRequestMessage request)
    {
        Uri uri = request.RequestUri ?? throw new InvalidOperationException($"{route.Method} {route.Pattern}: the row built a request without a URI.");
        string path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?')[0];
        TemplateMatcher matcher = new(TemplateParser.Parse(route.Pattern.Trim('/')), new RouteValueDictionary());

        Assert.True(
            matcher.TryMatch(new PathString("/" + path.Trim('/')), new RouteValueDictionary()),
            $"{route.Method} {route.Pattern}: the row sends {path}, which is not that route.");
        if (route.Method != AnyMethod)
        {
            Assert.True(route.Method == request.Method.Method, $"{route.Method} {route.Pattern}: the row sends a {request.Method.Method}.");
        }
    }

    // Every endpoint in the host's data source is a RouteEndpoint, with no framework-owned one among them: the OIDC
    // callbacks (/signin-oidc, /signout-callback-oidc) are authentication middleware, not endpoints, so they cannot appear.
    // A non-route endpoint fails the first assertion instead of being skipped, and is then a deliberate decision.
    [Fact]
    public void EveryMappedRouteHasARowInTheOutageTable()
    {
        IReadOnlyList<Endpoint> endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        Assert.Empty(endpoints.Where(endpoint => endpoint is not RouteEndpoint).Select(endpoint => endpoint.DisplayName));

        SortedSet<string> mapped = [.. endpoints.Cast<RouteEndpoint>().SelectMany(Keys)];
        SortedSet<string> documented = [.. Table.Select(route => Key(route.Method, route.Pattern))];

        Assert.Empty(mapped.Except(documented));
        Assert.Empty(documented.Except(mapped));
    }

    private static IEnumerable<string> Keys(RouteEndpoint endpoint) =>
        (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [AnyMethod]).Select(method => Key(method, endpoint.RoutePattern.RawText!));

    private static string Key(string method, string pattern) => method + " " + pattern;

    private static OutageRoute Customer(string method, string pattern, Func<BrowserSession, HttpRequestMessage> request, string why) =>
        new(method, pattern, CustomerRequestAsync(request), HttpStatusCode.ServiceUnavailable, DatabaseDown, why);

    private static OutageRoute Agent(string method, string pattern, Func<BrowserSession, HttpRequestMessage> request, string why) =>
        new(method, pattern, async fixture =>
        {
            (_, BrowserSession session) = await new AgentApi(fixture).NewAgentAsync();
            return request(session);
        }, HttpStatusCode.ServiceUnavailable, DatabaseDown, why);

    // A customer with a row in the database, as one whose database then went away.
    private static Func<ApiFactory, Task<HttpRequestMessage>> CustomerRequestAsync(Func<BrowserSession, HttpRequestMessage> request) =>
        async fixture => request(await SignInCustomerAsync(fixture));

    private static async Task<BrowserSession> SignInCustomerAsync(ApiFactory fixture) =>
        await BrowserSession.SignInAsync(fixture, (await TestCustomer.CreateAsync(fixture)).User);

    // The sid in the logout link is the session's own, so the link is read from the healthy host before the outage.
    private static async Task<HttpRequestMessage> LogoutRequestAsync(ApiFactory fixture)
    {
        BrowserSession session = await SignInCustomerAsync(fixture);
        using HttpResponseMessage user = await AgentApi.SendAsync(fixture, session.Request(HttpMethod.Get, "/bff/user"));
        string logoutUrl = (await JsonAsync(user)).GetProperty("logoutUrl").GetString()!;
        return session.Request(HttpMethod.Get, logoutUrl);
    }

    // Keycloak posts to the management port under its compose alias, with no cookie and no CSRF header.
    private static async Task<HttpRequestMessage> BackchannelLogoutRequestAsync(ApiFactory fixture)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(fixture);
        await BrowserSession.SignInAsync(fixture, customer.User);
        string token = fixture.IdentityProvider.LogoutToken(customer.User.SessionId, customer.User.Subject);
        return new HttpRequestMessage(HttpMethod.Post, ApiFactory.ManagementBaseUrl + BackchannelLogoutEndpoint.Path)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("logout_token", token)]),
        };
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static HttpRequestMessage Management(string path) => new(HttpMethod.Get, "http://localhost:" + ApiFactory.ManagementPort + path);

    /// <param name="Request">Builds the request, signing in on the healthy fixture host first when the route needs a session.</param>
    /// <param name="Code">The Problem Details code; null for a response that is not Problem Details.</param>
    /// <param name="Why">What the route reads (or does not), which is the reason for its status.</param>
    /// <param name="Verify">What else must hold of the response, for a route whose body or redirect is its proof.</param>
    private sealed record OutageRoute(
        string Method,
        string Pattern,
        Func<ApiFactory, Task<HttpRequestMessage>> Request,
        HttpStatusCode Status,
        string? Code,
        string Why,
        Func<HttpResponseMessage, Task>? Verify = null);
}
