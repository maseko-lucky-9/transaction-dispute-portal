using System.Net;
using System.Text.Json;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.IntegrationTests;

/// <summary>The agent work queue: roles, scope across customers, default filter, FIFO order, cursors and limits.</summary>
[Collection("api")]
public sealed class AgentQueueTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Epoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly AgentApi api = new(factory);

    [Fact]
    public async Task ACustomerIs403OnTheQueue()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        BrowserSession session = await BrowserSession.SignInAsync(factory, customer.User);

        Problem problem = await api.ProblemAsync(session.Request(HttpMethod.Get, AgentRequests.Queue), HttpStatusCode.Forbidden);

        Assert.Equal("forbidden", problem.Code);
    }

    [Fact]
    public async Task ADualRoleSessionIs403RoleConflictOnTheQueue()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        BrowserSession both = await BrowserSession.SignInAsync(factory, customer.User with { Roles = [Roles.Customer, Roles.DisputeAgent] });

        Problem problem = await api.ProblemAsync(both.Request(HttpMethod.Get, AgentRequests.Queue), HttpStatusCode.Forbidden);

        Assert.Equal(SingleRoleRequirement.Code, problem.Code);
    }

    [Fact]
    public async Task AnUnauthenticatedCallerIs401OnTheQueue()
    {
        using HttpClient browser = factory.CreateBrowserClient();
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRequests.Queue);
        request.Headers.Add(CsrfMiddleware.HeaderName, "1");

        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The queue is the one place the owner filter is lifted: two customers' disputes, each with only the display fields.
    [Fact]
    public async Task TheQueueListsDisputesOfEveryCustomerWithoutTheirSubjects()
    {
        (TestCustomer first, Guid firstId) = await api.SeedDisputeAsync(DisputeStatus.Submitted, Epoch);
        (TestCustomer second, Guid secondId) = await api.SeedDisputeAsync(DisputeStatus.UnderReview, Epoch.AddDays(1));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        List<JsonElement> items = await api.WalkQueueAsync(agent, string.Empty, seen => AgentApi.IdsOf(seen).Contains(secondId));

        Assert.Contains(firstId, AgentApi.IdsOf(items));
        Assert.Contains(secondId, AgentApi.IdsOf(items));
        JsonElement item = items.Single(candidate => candidate.GetProperty("id").GetGuid() == firstId);
        Assert.Equal("Test Customer", item.GetProperty("customerDisplayName").GetString());
        Assert.Equal("**** 0001", item.GetProperty("maskedAccountNumber").GetString());
        string raw = string.Concat(items.Select(candidate => candidate.GetRawText()));
        Assert.DoesNotContain(first.User.Subject, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(second.User.Subject, raw, StringComparison.Ordinal);
        Assert.False(item.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task WithoutAStatusTheQueueIsTheOpenOnesOnly()
    {
        Dictionary<DisputeStatus, Guid> seeded = [];
        foreach (DisputeStatus status in Enum.GetValues<DisputeStatus>())
        {
            seeded[status] = (await api.SeedDisputeAsync(status, Epoch.AddYears(1))).DisputeId;
        }

        (_, BrowserSession agent) = await api.NewAgentAsync();
        List<Guid> ids = AgentApi.IdsOf(await api.WalkQueueAsync(agent, string.Empty));

        Assert.Contains(seeded[DisputeStatus.Submitted], ids);
        Assert.Contains(seeded[DisputeStatus.UnderReview], ids);
        Assert.DoesNotContain(seeded[DisputeStatus.Accepted], ids);
        Assert.DoesNotContain(seeded[DisputeStatus.Rejected], ids);
        Assert.DoesNotContain(seeded[DisputeStatus.Withdrawn], ids);
    }

    [Fact]
    public async Task AStatusFilterIsCaseInsensitiveAndReturnsOnlyThatStatus()
    {
        Guid accepted = (await api.SeedDisputeAsync(DisputeStatus.Accepted, Epoch.AddYears(2))).DisputeId;
        Guid submitted = (await api.SeedDisputeAsync(DisputeStatus.Submitted, Epoch.AddYears(2))).DisputeId;
        (_, BrowserSession agent) = await api.NewAgentAsync();

        List<JsonElement> items = await api.WalkQueueAsync(agent, "?status=accepted");

        Assert.Contains(accepted, AgentApi.IdsOf(items));
        Assert.DoesNotContain(submitted, AgentApi.IdsOf(items));
        Assert.All(items, item => Assert.Equal("Accepted", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task TheQueueIsOldestFirst()
    {
        DateTimeOffset start = Epoch.AddYears(3);
        Guid newest = (await api.SeedDisputeAsync(DisputeStatus.Submitted, start.AddDays(2))).DisputeId;
        Guid oldest = (await api.SeedDisputeAsync(DisputeStatus.UnderReview, start)).DisputeId;
        Guid middle = (await api.SeedDisputeAsync(DisputeStatus.Submitted, start.AddDays(1))).DisputeId;
        (_, BrowserSession agent) = await api.NewAgentAsync();

        List<JsonElement> items = await api.WalkQueueAsync(agent, string.Empty, seen => AgentApi.IdsOf(seen).Contains(newest));

        List<Guid> ours = [.. AgentApi.IdsOf(items).Where(id => id == newest || id == oldest || id == middle)];
        Assert.Equal([oldest, middle, newest], ours);
        List<DateTimeOffset> created = [.. items.Select(item => item.GetProperty("createdAt").GetDateTimeOffset())];
        Assert.Equal(created.Order(), created);
    }

    // Some share one instant (the id breaks the tie), pages of 2: every dispute exactly once, none skipped, ours in time order.
    [Fact]
    public async Task CursorPagingRepeatsAndSkipsNothing()
    {
        DateTimeOffset start = Epoch.AddYears(4);
        (TestCustomer customer, Guid first) = await api.SeedDisputeAsync(DisputeStatus.Submitted, start);
        DateTimeOffset shared = start.AddDays(1);
        List<Guid> sameInstant =
        [
            await api.SeedAnotherAsync(customer, DisputeStatus.Submitted, shared),
            await api.SeedAnotherAsync(customer, DisputeStatus.UnderReview, shared),
            await api.SeedAnotherAsync(customer, DisputeStatus.Submitted, shared),
        ];
        Guid last = await api.SeedAnotherAsync(customer, DisputeStatus.Submitted, start.AddDays(2));
        List<Guid> expected = [first, .. sameInstant, last];
        (_, BrowserSession agent) = await api.NewAgentAsync();

        List<Guid> seen = AgentApi.IdsOf(await api.WalkQueueAsync(agent, "?limit=2", items => expected.TrueForAll(AgentApi.IdsOf(items).Contains)));

        Assert.Equal(seen.Count, seen.Distinct().Count());
        List<Guid> ours = [.. seen.Where(expected.Contains)];
        Assert.Equal(expected.Count, ours.Count);
        Assert.Equal(first, ours[0]);
        Assert.Equal(last, ours[^1]);
    }

    [Fact]
    public async Task APageHoldsAtMostTheLimitAndSaysWhenThereIsMore()
    {
        (TestCustomer customer, _) = await api.SeedDisputeAsync(DisputeStatus.Accepted, Epoch.AddYears(5));
        await api.SeedAnotherAsync(customer, DisputeStatus.Accepted, Epoch.AddYears(5).AddDays(1));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        JsonElement page = await api.OkAsync(agent, AgentRequests.Queue + "?status=Accepted&limit=1");

        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.NotNull(page.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task ATamperedForeignOrRefilteredCursorIs400InvalidCursor()
    {
        (TestCustomer customer, _) = await api.SeedDisputeAsync(DisputeStatus.Submitted, Epoch.AddYears(6));
        await api.SeedAnotherAsync(customer, DisputeStatus.Submitted, Epoch.AddYears(6).AddDays(1));
        (_, BrowserSession agent) = await api.NewAgentAsync();
        (_, BrowserSession other) = await api.NewAgentAsync();
        string cursor = (await api.OkAsync(agent, AgentRequests.Queue + "?limit=1")).GetProperty("nextCursor").GetString()!;
        char[] tampered = cursor.ToCharArray();
        tampered[tampered.Length / 2] = tampered[tampered.Length / 2] == 'A' ? 'B' : 'A';

        await AssertInvalidCursorAsync(agent, $"?limit=1&cursor={Uri.EscapeDataString(new string(tampered))}");
        await AssertInvalidCursorAsync(other, $"?limit=1&cursor={Uri.EscapeDataString(cursor)}");
        await AssertInvalidCursorAsync(agent, $"?limit=1&status=Submitted&cursor={Uri.EscapeDataString(cursor)}");
        await AssertInvalidCursorAsync(agent, "?cursor=not-a-cursor");
        Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(agent.Request(HttpMethod.Get, AgentRequests.Queue + "?cursor="))).StatusCode);
    }

    [Theory]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=101", "limit")]
    [InlineData("limit=-1", "limit")]
    [InlineData("limit=abc", "limit")]
    [InlineData("status=Done", "status")]
    public async Task ABadQueryIs400WithTheFieldNamed(string query, string field)
    {
        (_, BrowserSession agent) = await api.NewAgentAsync();

        Problem problem = await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.Queue + "?" + query), HttpStatusCode.BadRequest);

        Assert.True(problem.Body.GetProperty("errors").TryGetProperty(field, out _), "errors." + field);
    }

    private async Task AssertInvalidCursorAsync(BrowserSession session, string query)
    {
        Problem problem = await api.ProblemAsync(session.Request(HttpMethod.Get, AgentRequests.Queue + query), HttpStatusCode.BadRequest);

        Assert.Equal("invalid_cursor", problem.Code);
    }
}
