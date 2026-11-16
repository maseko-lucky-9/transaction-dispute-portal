using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// ADR-019: starting a review assigns the dispute to that agent; only they may accept, reject or release it (409
/// not_assignee otherwise, nothing changed); every way out clears the assignee; the queue filters by assignment.
/// </summary>
[Collection("api")]
public sealed class AgentAssignmentTests(ApiFactory factory)
{
    private const string Note = "Checked with the merchant.";

    private readonly AgentApi api = new(factory);

    [Fact]
    public async Task StartingAReviewAssignsItToThatAgentAndTheDetailSaysYou()
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        (IdpUser reviewer, BrowserSession agent) = await api.NewAgentAsync();
        (_, BrowserSession colleague) = await api.NewAgentAsync();

        HttpResponseMessage response = await api.SendAsync(AgentRequests.Transition(agent, id, await api.ETagAsync(agent, id), "UnderReview"));
        JsonElement claimed = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement seenByColleague = await api.OkAsync(colleague, AgentRequests.Detail(id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("You", claimed.GetProperty("assignment").GetString());
        Assert.Equal(reviewer.Subject, claimed.GetProperty("assigneeId").GetString());
        Assert.Equal(claimed.GetProperty("updatedAt").GetDateTimeOffset(), claimed.GetProperty("claimedAt").GetDateTimeOffset());
        Assert.Equal("AnotherAgent", seenByColleague.GetProperty("assignment").GetString());
        Assert.Equal(reviewer.Subject, seenByColleague.GetProperty("assigneeId").GetString());
        Assert.Equal(("UnderReview", reviewer.Subject), await StateAsync(id));
    }

    [Theory]
    [InlineData("Accepted", Note)]
    [InlineData("Rejected", Note)]
    [InlineData("Submitted", null)]
    public async Task AnotherAgentIs409NotAssigneeOnDecideAndReleaseAndNothingChanges(string to, string? note)
    {
        (IdpUser reviewer, _) = await api.NewAgentAsync();
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview, assignee: reviewer.Subject);
        (_, BrowserSession intruder) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(intruder, id);
        long history = await HistoryCountAsync(id);

        Problem problem = await api.ProblemAsync(AgentRequests.Transition(intruder, id, etag, to, note), HttpStatusCode.Conflict);

        Assert.Equal("not_assignee", problem.Code);
        Assert.DoesNotContain(reviewer.Subject, problem.Body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(("UnderReview", reviewer.Subject), await StateAsync(id));
        Assert.Equal(history, await HistoryCountAsync(id));
        Assert.Equal(etag, await api.ETagAsync(intruder, id));
    }

    [Fact]
    public async Task TheAssigneeReleasesItBackToTheQueueUnclaimed()
    {
        DateTimeOffset createdAt = UniqueInstant();
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.Submitted, createdAt);
        (IdpUser reviewer, BrowserSession agent) = await api.NewAgentAsync();
        HttpResponseMessage claim = await api.SendAsync(AgentRequests.Transition(agent, id, await api.ETagAsync(agent, id), "UnderReview"));

        HttpResponseMessage release = await api.SendAsync(AgentRequests.Transition(agent, id, DisputeRequests.ETagOf(claim), "Submitted"));
        JsonElement released = await release.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        Assert.Equal(await api.ETagAsync(agent, id), DisputeRequests.ETagOf(release));
        Assert.Equal("Submitted", released.GetProperty("status").GetString());
        Assert.Equal("Unclaimed", released.GetProperty("assignment").GetString());
        Assert.Equal(JsonValueKind.Null, released.GetProperty("assigneeId").ValueKind);
        Assert.Equal(JsonValueKind.Null, released.GetProperty("claimedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, released.GetProperty("resolvedAt").ValueKind);
        JsonElement last = released.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal(("UnderReview", "Submitted", "Agent", reviewer.Subject), (
            last.GetProperty("from").GetString(), last.GetProperty("to").GetString(), last.GetProperty("actorType").GetString(), last.GetProperty("actorId").GetString()));
        Assert.Equal(JsonValueKind.Null, last.GetProperty("note").ValueKind);
        Assert.Equal(("Submitted", (string?)null), await StateAsync(id));
        Assert.Equal(3, await HistoryCountAsync(id));
        JsonElement listed = (await WalkUntilPastAsync(agent, "?assignment=unclaimed", createdAt)).Single(item => item.GetProperty("id").GetGuid() == id);
        Assert.Equal("Unclaimed", listed.GetProperty("assignment").GetString());
    }

    [Fact]
    public async Task AReleaseWithANoteIs400AndNothingChanges()
    {
        (IdpUser reviewer, BrowserSession agent) = await api.NewAgentAsync();
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview, assignee: reviewer.Subject);

        Problem problem = await api.ProblemAsync(
            AgentRequests.Transition(agent, id, await api.ETagAsync(agent, id), "Submitted", "Going on leave tomorrow."), HttpStatusCode.BadRequest);

        Assert.Equal("validation_failed", problem.Code);
        Assert.True(problem.Body.GetProperty("errors").TryGetProperty("note", out _));
        Assert.Equal(("UnderReview", reviewer.Subject), await StateAsync(id));
    }

    [Fact]
    public async Task AfterAReleaseAnyAgentCanClaimAndDecideIt()
    {
        (_, Guid id) = await api.SeedDisputeAsync();
        (_, BrowserSession first) = await api.NewAgentAsync();
        (IdpUser next, BrowserSession second) = await api.NewAgentAsync();
        HttpResponseMessage claim = await api.SendAsync(AgentRequests.Transition(first, id, await api.ETagAsync(first, id), "UnderReview"));
        HttpResponseMessage release = await api.SendAsync(AgentRequests.Transition(first, id, DisputeRequests.ETagOf(claim), "Submitted"));

        HttpResponseMessage reclaim = await api.SendAsync(AgentRequests.Transition(second, id, DisputeRequests.ETagOf(release), "UnderReview"));
        JsonElement reclaimed = await reclaim.Content.ReadFromJsonAsync<JsonElement>();
        HttpResponseMessage accept = await api.SendAsync(AgentRequests.Transition(second, id, DisputeRequests.ETagOf(reclaim), "Accepted", Note));

        Assert.Equal(HttpStatusCode.OK, reclaim.StatusCode);
        Assert.Equal(next.Subject, reclaimed.GetProperty("assigneeId").GetString());
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(("Accepted", (string?)null), await StateAsync(id));
    }

    [Fact]
    public async Task ACustomerWithdrawalClearsTheAssignee()
    {
        (IdpUser reviewer, BrowserSession agent) = await api.NewAgentAsync();
        (TestCustomer customer, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview, assignee: reviewer.Subject);
        BrowserSession owner = await BrowserSession.SignInAsync(factory, customer.User);

        HttpResponseMessage withdrawn = await api.SendAsync(DisputeRequests.Withdraw(owner, id, await api.ETagAsync(agent, id)));
        JsonElement seenByAgent = await api.OkAsync(agent, AgentRequests.Detail(id));

        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal(("Withdrawn", (string?)null), await StateAsync(id));
        Assert.Equal(JsonValueKind.Null, seenByAgent.GetProperty("assignment").ValueKind);
        Assert.Equal(JsonValueKind.Null, seenByAgent.GetProperty("assigneeId").ValueKind);
    }

    // One instant for all of them, older than anything else this test reads, so each walk can stop once it is past it.
    [Fact]
    public async Task TheQueueFiltersByAssignmentRelativeToTheAgentAsking()
    {
        DateTimeOffset at = UniqueInstant();
        (IdpUser me, BrowserSession agent) = await api.NewAgentAsync();
        (TestCustomer customer, Guid unclaimed) = await api.SeedDisputeAsync(DisputeStatus.Submitted, at);
        Guid mine = await api.SeedAnotherAsync(customer, DisputeStatus.UnderReview, at, assignee: me.Subject);
        Guid others = await api.SeedAnotherAsync(customer, DisputeStatus.UnderReview, at, assignee: "agent-someone-else");
        Guid legacy = await api.SeedAnotherAsync(customer, DisputeStatus.UnderReview, at);
        Guid decided = await api.SeedAnotherAsync(customer, DisputeStatus.Accepted, at);
        // A row put under review by a version older than the assignee column: no assignee recorded.
        await AdminSql.CountAsync(factory.AdminConnectionString, "WITH changed AS (UPDATE disputes SET assignee_id = NULL WHERE id = @id RETURNING 1) SELECT count(*) FROM changed", ("id", legacy));
        Guid[] ours = [unclaimed, mine, others, legacy, decided];

        Dictionary<Guid, JsonElement> unclaimedPage = Ours(await WalkUntilPastAsync(agent, "?assignment=unclaimed", at), ours);
        Dictionary<Guid, JsonElement> minePage = Ours(await WalkUntilPastAsync(agent, "?assignment=MINE", at), ours);
        Dictionary<Guid, JsonElement> othersPage = Ours(await WalkUntilPastAsync(agent, "?assignment=Others", at), ours);
        Dictionary<Guid, JsonElement> openPage = Ours(await WalkUntilPastAsync(agent, string.Empty, at), ours);

        Assert.Equal([unclaimed], unclaimedPage.Keys);
        Assert.Equal([mine], minePage.Keys);
        Assert.Equal(new HashSet<Guid> { others, legacy }, othersPage.Keys.ToHashSet());
        Assert.Equal(new HashSet<Guid> { unclaimed, mine, others, legacy }, openPage.Keys.ToHashSet());
        Assert.Equal(("Unclaimed", JsonValueKind.Null), (unclaimedPage[unclaimed].GetProperty("assignment").GetString(), unclaimedPage[unclaimed].GetProperty("claimedAt").ValueKind));
        Assert.Equal(("You", me.Subject), (minePage[mine].GetProperty("assignment").GetString(), minePage[mine].GetProperty("assigneeId").GetString()));
        Assert.Equal(at, minePage[mine].GetProperty("claimedAt").GetDateTimeOffset());
        Assert.Equal(("AnotherAgent", "agent-someone-else"), (othersPage[others].GetProperty("assignment").GetString(), othersPage[others].GetProperty("assigneeId").GetString()));
        Assert.Equal(("AnotherAgent", JsonValueKind.Null), (othersPage[legacy].GetProperty("assignment").GetString(), othersPage[legacy].GetProperty("assigneeId").ValueKind));
    }

    [Theory]
    [InlineData("assignment=mine&status=UnderReview")]
    [InlineData("status=Submitted&assignment=unclaimed")]
    [InlineData("assignment=yours")]
    [InlineData("assignment=")]
    public async Task ABadOrCombinedAssignmentIs400OnTheAssignmentField(string query)
    {
        (_, BrowserSession agent) = await api.NewAgentAsync();

        Problem problem = await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.Queue + "?" + query), HttpStatusCode.BadRequest);

        Assert.Equal("validation_failed", problem.Code);
        Assert.True(problem.Body.GetProperty("errors").TryGetProperty("assignment", out _), "errors.assignment");
    }

    [Fact]
    public async Task ACursorIssuedForOneAssignmentIsRefusedForAnother()
    {
        DateTimeOffset at = UniqueInstant();
        (TestCustomer customer, _) = await api.SeedDisputeAsync(DisputeStatus.Submitted, at);
        await api.SeedAnotherAsync(customer, DisputeStatus.Submitted, at);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        string cursor = Uri.EscapeDataString((await api.OkAsync(agent, AgentRequests.Queue + "?assignment=unclaimed&limit=1")).GetProperty("nextCursor").GetString()!);
        string open = Uri.EscapeDataString((await api.OkAsync(agent, AgentRequests.Queue + "?limit=1")).GetProperty("nextCursor").GetString()!);

        Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(agent.Request(HttpMethod.Get, AgentRequests.Queue + "?assignment=Unclaimed&limit=1&cursor=" + cursor))).StatusCode);
        await AssertInvalidCursorAsync(agent, "?assignment=others&limit=1&cursor=" + cursor);
        await AssertInvalidCursorAsync(agent, "?assignment=mine&limit=1&cursor=" + cursor);
        await AssertInvalidCursorAsync(agent, "?limit=1&cursor=" + cursor);
        await AssertInvalidCursorAsync(agent, "?status=Submitted&limit=1&cursor=" + cursor);
        await AssertInvalidCursorAsync(agent, "?assignment=unclaimed&limit=1&cursor=" + open);
    }

    [Fact]
    public async Task TheRunbookBreakGlassReleaseReturnsAStaleClaimToTheQueueOnce()
    {
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview, assignee: "agent-who-left");
        (IdpUser operatorAgent, BrowserSession agent) = await api.NewAgentAsync();

        string sql = RunbookBreakGlassReleaseSql();
        int first = await RunAsOwnerAsync(sql, id, operatorAgent.Subject);
        int again = await RunAsOwnerAsync(sql, id, operatorAgent.Subject);
        JsonElement released = await api.OkAsync(agent, AgentRequests.Detail(id));

        Assert.Equal((1, 0), (first, again));
        Assert.Equal(("Submitted", (string?)null), await StateAsync(id));
        Assert.Equal(2, await HistoryCountAsync(id));
        Assert.Equal("Unclaimed", released.GetProperty("assignment").GetString());
        JsonElement last = released.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal(("UnderReview", "Submitted", operatorAgent.Subject), (
            last.GetProperty("from").GetString(), last.GetProperty("to").GetString(), last.GetProperty("actorId").GetString()));
    }

    [Fact]
    public async Task ACustomerADualRoleSessionAndAnAnonymousCallerCannotListMineAndNothingIsAudited()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        BrowserSession plain = await BrowserSession.SignInAsync(factory, customer.User);
        BrowserSession both = await BrowserSession.SignInAsync(factory, customer.User with { Roles = [Roles.Customer, Roles.DisputeAgent] });
        using MeasurementProbe<long> audits = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, "audit.events");
        const string Mine = AgentRequests.Queue + "?assignment=mine";

        Problem forbidden = await api.ProblemAsync(plain.Request(HttpMethod.Get, Mine), HttpStatusCode.Forbidden);
        Problem conflict = await api.ProblemAsync(both.Request(HttpMethod.Get, Mine), HttpStatusCode.Forbidden);
        using HttpClient browser = factory.CreateBrowserClient();
        using HttpRequestMessage unauthenticated = new(HttpMethod.Get, Mine);
        unauthenticated.Headers.Add(CsrfMiddleware.HeaderName, "1");
        HttpResponseMessage anonymous = await browser.SendAsync(unauthenticated);

        Assert.Equal("forbidden", forbidden.Code);
        Assert.Equal(SingleRoleRequirement.Code, conflict.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(audits.Samples);
    }

    [Fact]
    public async Task MineIsEmptyForAnAgentWithNoClaimsWhileOthersAndUnclaimedListTheirOwn()
    {
        DateTimeOffset at = UniqueInstant();
        (TestCustomer customer, Guid unclaimed) = await api.SeedDisputeAsync(DisputeStatus.Submitted, at);
        // Someone else's claim exists, so "mine" ignoring the assignee would not be empty.
        Guid colleagues = await api.SeedAnotherAsync(customer, DisputeStatus.UnderReview, at, assignee: "agent-" + Guid.NewGuid().ToString("N"));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        JsonElement mine = await api.OkAsync(agent, AgentRequests.Queue + "?assignment=mine&limit=100");
        List<JsonElement> others = await WalkUntilPastAsync(agent, "?assignment=others", at);
        List<JsonElement> open = await WalkUntilPastAsync(agent, "?assignment=unclaimed", at);

        Assert.Equal(0, mine.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("nextCursor").ValueKind);
        Assert.Contains(colleagues, AgentApi.IdsOf(others));
        Assert.DoesNotContain(unclaimed, AgentApi.IdsOf(others));
        Assert.All(others, item => Assert.Equal(("UnderReview", "AnotherAgent"), (item.GetProperty("status").GetString(), item.GetProperty("assignment").GetString())));
        Assert.Contains(unclaimed, AgentApi.IdsOf(open));
        Assert.DoesNotContain(colleagues, AgentApi.IdsOf(open));
        Assert.All(open, item => Assert.Equal(("Submitted", "Unclaimed"), (item.GetProperty("status").GetString(), item.GetProperty("assignment").GetString())));
    }

    // ADR-019: the customer sees a release as a move back to Submitted by "an agent": no subject, no note, no assignee.
    [Fact]
    public async Task AfterAReleaseTheCustomerSeesAMoveBackToSubmittedWithNoAgentNoNoteAndNoAssignee()
    {
        (TestCustomer customer, Guid id) = await api.SeedDisputeAsync();
        (IdpUser reviewer, BrowserSession agent) = await api.NewAgentAsync();
        HttpResponseMessage claim = await api.SendAsync(AgentRequests.Transition(agent, id, await api.ETagAsync(agent, id), "UnderReview"));
        HttpResponseMessage release = await api.SendAsync(AgentRequests.Transition(agent, id, DisputeRequests.ETagOf(claim), "Submitted"));
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        BrowserSession owner = await BrowserSession.SignInAsync(factory, customer.User);

        HttpResponseMessage response = await api.SendAsync(owner.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{id}"));
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement detail = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Submitted", detail.GetProperty("status").GetString());
        JsonElement last = detail.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal(("UnderReview", "Submitted", "Agent"), (
            last.GetProperty("from").GetString(), last.GetProperty("to").GetString(), last.GetProperty("actorType").GetString()));
        Assert.True(!last.TryGetProperty("note", out JsonElement note) || note.ValueKind == JsonValueKind.Null, "no note on a release");
        Assert.Equal(new[] { "actorType", "from", "note", "occurredAt", "to" }, last.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(reviewer.Subject, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(detail.EnumerateObject(), property => property.Name.Contains("assign", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("claimed", StringComparison.OrdinalIgnoreCase));
    }

    // The assignee releases and decides at once, both with the same ETag: both pass If-Match at the gate, one write lands
    // and the other fails the xmin check. Whichever wins, the dispute is consistent and nobody holds it.
    [Fact]
    public async Task AReleaseRacingTheAssigneesOwnDecisionHasOneWinnerAndLeavesNoAssignee()
    {
        (IdpUser reviewer, BrowserSession agent) = await api.NewAgentAsync();
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview, assignee: reviewer.Subject);
        string etag = await api.ETagAsync(agent, id);
        long history = await HistoryCountAsync(id);
        await using WebApplicationFactory<Program> gated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            Rendezvous afterFind = new(2);
            services.AddScoped<AgentDisputeRepository>();
            services.AddScoped<IAgentDisputeRepository>(provider =>
                new GatedAgentDisputeRepository(provider.GetRequiredService<AgentDisputeRepository>(), afterFind));
        }));

        HttpResponseMessage[] responses = await Task.WhenAll(
            AgentApi.SendAsync(gated, AgentRequests.Transition(agent, id, etag, "Submitted")),
            AgentApi.SendAsync(gated, AgentRequests.Transition(agent, id, etag, "Accepted", Note)));

        HttpResponseMessage won = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        HttpResponseMessage lost = Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
        string lostCode = (await Problem.ReadAsync(lost)).Code;
        Assert.True(
            (lost.StatusCode, lostCode) is (HttpStatusCode.PreconditionFailed, "precondition_failed") or (HttpStatusCode.Conflict, "invalid_transition"),
            $"{(int)lost.StatusCode} {lostCode}");
        string winner = won == responses[0] ? "Submitted" : "Accepted";
        Assert.Equal((winner, (string?)null), await StateAsync(id));
        Assert.Equal(history + 1, await HistoryCountAsync(id));
    }

    // docs/runbook.md, "Stale claim": the statement between BEGIN; and the "Expect" line after the break-glass-release
    // marker, with parameters for the psql variables, so the test runs exactly what the runbook tells an operator to.
    private static string RunbookBreakGlassReleaseSql()
    {
        string runbook = File.ReadAllText(RepositoryFiles.PathOf("docs", "runbook.md"));
        int marker = runbook.IndexOf("-- break-glass-release:", StringComparison.Ordinal);
        Assert.True(marker >= 0, "the break-glass-release marker in docs/runbook.md");
        int begin = runbook.IndexOf("BEGIN;\n", marker, StringComparison.Ordinal) + "BEGIN;\n".Length;
        int end = runbook.IndexOf("-- Expect", begin, StringComparison.Ordinal);
        Assert.True(begin > marker && end > begin, "the break-glass statement in docs/runbook.md");
        return runbook[begin..end].Replace(":'id'", "@id", StringComparison.Ordinal).Replace(":'operator'", "@operator", StringComparison.Ordinal);
    }

    // Older than anything other tests seed (they use 2001 onwards or now), and distinct per call.
    private static DateTimeOffset UniqueInstant() =>
        new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(Random.Shared.Next(0, 500_000));

    private static Dictionary<Guid, JsonElement> Ours(IEnumerable<JsonElement> items, Guid[] ours) =>
        items.Where(item => ours.Contains(item.GetProperty("id").GetGuid())).ToDictionary(item => item.GetProperty("id").GetGuid());

    // Oldest first: once an item is newer than the instant, every dispute created at it has been listed.
    private Task<List<JsonElement>> WalkUntilPastAsync(BrowserSession agent, string query, DateTimeOffset at) =>
        api.WalkQueueAsync(agent, query, seen => seen.Exists(item => item.GetProperty("createdAt").GetDateTimeOffset() > at));

    private async Task AssertInvalidCursorAsync(BrowserSession agent, string query) =>
        Assert.Equal("invalid_cursor", (await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.Queue + query), HttpStatusCode.BadRequest)).Code);

    private async Task<(string Status, string? Assignee)> StateAsync(Guid id) =>
        (await AdminSql.ListAsync(
            factory.AdminConnectionString,
            "SELECT status, assignee_id FROM disputes WHERE id = @id",
            reader => (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)),
            ("id", id))).Single();

    private async Task<int> RunAsOwnerAsync(string sql, Guid id, string operatorSubject)
    {
        await using NpgsqlConnection connection = new(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using NpgsqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("operator", operatorSubject);
        int inserted = await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return inserted;
    }

    private Task<long> HistoryCountAsync(Guid id) =>
        AdminSql.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id));
}
