using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.Net.Http.Headers;

namespace DisputePortal.IntegrationTests;

/// <summary>Preconditions (428/412/409), ownership (BOLA), roles, cursors, the timeline's privacy and the submit rate limit.</summary>
[Collection("api")]
public sealed class DisputeSecurityTests(ApiFactory api)
{
    private const string SomeDispute = DisputeRequests.Disputes + "/c0570000-0000-4000-8000-000000000001";

    // Ownership before preconditions: a foreign or missing id is 404 even without If-Match, so 428 cannot probe for ids.
    [Fact]
    public async Task WithdrawingAForeignOrMissingIdWithoutIfMatchIs404Not428()
    {
        (_, Guid theirs, _, _) = await SubmittedAsync();
        (_, _, _, BrowserSession intruder) = await SubmittedAsync();

        Problem foreign = await ProblemAsync(DisputeRequests.Withdraw(intruder, theirs, ifMatch: null), HttpStatusCode.NotFound);
        Problem missing = await ProblemAsync(DisputeRequests.Withdraw(intruder, Guid.NewGuid(), ifMatch: null), HttpStatusCode.NotFound);

        Assert.Equal((missing.Code, missing.Detail), (foreign.Code, foreign.Detail));
    }

    [Fact]
    public async Task WithdrawingWithoutIfMatchIs428()
    {
        (_, Guid id, _, BrowserSession session) = await SubmittedAsync();

        Problem problem = await ProblemAsync(DisputeRequests.Withdraw(session, id, ifMatch: null), HttpStatusCode.PreconditionRequired);

        Assert.Equal("precondition_required", problem.Code);
    }

    // Weak tags never match (strong comparison), and "*" is refused: a change must name the version it was decided on.
    [Theory]
    [InlineData("\"1\"")]
    [InlineData("W/\"{0}\"")]
    [InlineData("*")]
    [InlineData("not-an-etag")]
    public async Task AStaleOrLooseIfMatchIs412WithTheCurrentETag(string ifMatchTemplate)
    {
        (_, Guid id, string etag, BrowserSession session) = await SubmittedAsync();
        string ifMatch = ifMatchTemplate.Replace("{0}", etag.Trim('"'), StringComparison.Ordinal);

        HttpResponseMessage response = await SendAsync(DisputeRequests.Withdraw(session, id, ifMatch));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(etag, DisputeRequests.ETagOf(response));
        Assert.Equal("precondition_failed", (await Problem.ReadAsync(response)).Code);
        Assert.Equal("Submitted", (await DetailAsync(session, id)).GetProperty("status").GetString());
    }

    // Stale wins over terminal (every change changes the tag); with the fresh tag, a terminal dispute is 409.
    [Fact]
    public async Task AfterAWithdrawalTheOldETagIs412AndTheNewOne409InvalidTransition()
    {
        (_, Guid id, string etag, BrowserSession session) = await SubmittedAsync();
        HttpResponseMessage withdrawn = await SendAsync(DisputeRequests.Withdraw(session, id, etag));
        string fresh = DisputeRequests.ETagOf(withdrawn)!;

        HttpResponseMessage stale = await SendAsync(DisputeRequests.Withdraw(session, id, etag));
        Problem terminal = await ProblemAsync(DisputeRequests.Withdraw(session, id, fresh), HttpStatusCode.Conflict);

        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(fresh, DisputeRequests.ETagOf(stale));
        Assert.Equal("invalid_transition", terminal.Code);
    }

    [Fact]
    public async Task TheDetailCarriesTheSameETagAsTheSubmission()
    {
        (_, Guid id, string etag, BrowserSession session) = await SubmittedAsync();

        HttpResponseMessage detail = await SendAsync(session.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{id}"));

        Assert.Equal(etag, DisputeRequests.ETagOf(detail));
    }

    [Fact]
    public async Task AnotherCustomersDisputeIs404OnReadAndWithdrawLikeAMissingOne()
    {
        (_, Guid theirs, string etag, BrowserSession owner) = await SubmittedAsync();
        (_, _, _, BrowserSession intruder) = await SubmittedAsync();

        Problem read = await ProblemAsync(intruder.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{theirs}"), HttpStatusCode.NotFound);
        Problem missing = await ProblemAsync(intruder.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
        Problem withdraw = await ProblemAsync(DisputeRequests.Withdraw(intruder, theirs, etag), HttpStatusCode.NotFound);

        Assert.Equal((missing.Code, missing.Detail), (read.Code, read.Detail));
        Assert.Equal(missing.Code, withdraw.Code);
        Assert.Equal("Submitted", (await DetailAsync(owner, theirs)).GetProperty("status").GetString());
        Assert.DoesNotContain(theirs.ToString(), (await DetailListAsync(intruder)).GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GET", DisputeRequests.Reasons)]
    [InlineData("GET", DisputeRequests.Disputes)]
    [InlineData("POST", DisputeRequests.Disputes)]
    [InlineData("GET", SomeDispute)]
    [InlineData("POST", SomeDispute + "/withdraw")]
    public async Task AnAgentIs403OnEveryCustomerDisputeRoute(string method, string path)
    {
        BrowserSession agent = await BrowserSession.SignInAsync(api, new IdpUser(Seeder.AgentSubject, "Agent", [Roles.DisputeAgent]));

        Assert.Equal("forbidden", (await ProblemAsync(Request(agent, method, path), HttpStatusCode.Forbidden)).Code);
    }

    [Theory]
    [InlineData("GET", DisputeRequests.Reasons)]
    [InlineData("GET", DisputeRequests.Disputes)]
    [InlineData("POST", DisputeRequests.Disputes)]
    [InlineData("GET", SomeDispute)]
    [InlineData("POST", SomeDispute + "/withdraw")]
    public async Task ADualRoleSessionIs403RoleConflictOnEveryCustomerDisputeRoute(string method, string path)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        BrowserSession both = await BrowserSession.SignInAsync(api, customer.User with { Roles = [Roles.Customer, Roles.DisputeAgent] });

        Assert.Equal(SingleRoleRequirement.Code, (await ProblemAsync(Request(both, method, path), HttpStatusCode.Forbidden)).Code);
    }

    // A customer sees who acted only as a type: never a subject, theirs or an agent's (checked on the raw JSON).
    [Fact]
    public async Task TheTimelineNeverCarriesActorIds()
    {
        (TestCustomer customer, Guid id, string etag, BrowserSession session) = await SubmittedAsync();
        HttpResponseMessage withdrawn = await SendAsync(DisputeRequests.Withdraw(session, id, etag));

        string raw = await withdrawn.Content.ReadAsStringAsync();
        string detail = await (await SendAsync(session.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{id}"))).Content.ReadAsStringAsync();

        Assert.Equal(2, await AdminSql.CountAsync(api.AdminConnectionString,
            "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id AND actor_id = @subject", ("id", id), ("subject", customer.User.Subject)));
        Assert.All(new[] { raw, detail }, json =>
        {
            Assert.DoesNotContain(customer.User.Subject, json, StringComparison.Ordinal);
            Assert.DoesNotContain("actorId", json, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ATamperedForeignOrRefilteredCursorIs400InvalidCursor()
    {
        (TestCustomer customer, _, _, BrowserSession session) = await SubmittedAsync();
        await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), Domain.Disputes.DisputeStatus.Submitted);
        (_, _, _, BrowserSession other) = await SubmittedAsync();
        string cursor = (await OkAsync(session, DisputeRequests.Disputes + "?limit=1")).GetProperty("nextCursor").GetString()!;
        char[] tampered = cursor.ToCharArray();
        tampered[tampered.Length / 2] = tampered[tampered.Length / 2] == 'A' ? 'B' : 'A';

        await AssertInvalidCursorAsync(session, $"?limit=1&cursor={Uri.EscapeDataString(new string(tampered))}");
        await AssertInvalidCursorAsync(other, $"?limit=1&cursor={Uri.EscapeDataString(cursor)}");
        await AssertInvalidCursorAsync(session, $"?limit=1&status=Submitted&cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(session.Request(HttpMethod.Get, DisputeRequests.Disputes + "?cursor="))).StatusCode);
    }

    // The tighter bucket on submit: 10 a minute per subject, then 429 with Retry-After (rejected bodies count too).
    [Fact]
    public async Task TheEleventhSubmitInAMinuteIs429WithRetryAfter()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);
        List<HttpStatusCode> statuses = [];
        for (int attempt = 0; attempt < 10; attempt++)
        {
            statuses.Add((await SendAsync(DisputeRequests.SubmitJson(session, "{}"))).StatusCode);
        }

        HttpResponseMessage limited = await SendAsync(DisputeRequests.SubmitJson(session, "{}"));
        HttpResponseMessage reads = await SendAsync(session.Request(HttpMethod.Get, DisputeRequests.Disputes));

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.BadRequest, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(RateLimits.RejectionCode, (await Problem.ReadAsync(limited)).Code);
        Assert.Equal(HttpStatusCode.OK, reads.StatusCode);
    }

    private static HttpRequestMessage Request(BrowserSession session, string method, string path)
    {
        HttpRequestMessage request = session.Request(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
            request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, "\"1\"");
        }

        return request;
    }

    private async Task<(TestCustomer Customer, Guid DisputeId, string ETag, BrowserSession Session)> SubmittedAsync()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid transactionId = await customer.AddEligibleTransactionAsync(api);
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);
        Guid id = Guid.NewGuid();
        HttpResponseMessage created = await SendAsync(DisputeRequests.Submit(session, id, transactionId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (customer, id, DisputeRequests.ETagOf(created)!, session);
    }

    private async Task AssertInvalidCursorAsync(BrowserSession session, string query) =>
        Assert.Equal("invalid_cursor", (await ProblemAsync(session.Request(HttpMethod.Get, DisputeRequests.Disputes + query), HttpStatusCode.BadRequest)).Code);

    private Task<JsonElement> DetailAsync(BrowserSession session, Guid id) => OkAsync(session, $"{DisputeRequests.Disputes}/{id}");

    private Task<JsonElement> DetailListAsync(BrowserSession session) => OkAsync(session, DisputeRequests.Disputes);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using HttpClient browser = api.CreateBrowserClient();
        return await browser.SendAsync(request);
    }

    private async Task<JsonElement> OkAsync(BrowserSession session, string path)
    {
        HttpResponseMessage response = await SendAsync(session.Request(HttpMethod.Get, path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Problem> ProblemAsync(HttpRequestMessage request, HttpStatusCode status)
    {
        HttpResponseMessage response = await SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        return await Problem.ReadAsync(response);
    }
}
