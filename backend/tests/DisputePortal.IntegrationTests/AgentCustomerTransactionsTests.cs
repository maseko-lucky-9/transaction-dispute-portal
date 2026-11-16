using System.Net;
using System.Text.Json;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The disputed customer's transactions an agent reads beside a dispute: roles, the scope (this customer, this window),
/// order, paging, what each row carries, and the cursor's binding to the agent and the dispute.
/// </summary>
[Collection("api")]
public sealed class AgentCustomerTransactionsTests(ApiFactory factory)
{
    private static readonly string[] ItemFields =
        ["amount", "category", "currency", "direction", "dispute", "id", "maskedAccountNumber", "merchantName", "postedAt", "reference", "status", "transactionDate"];

    private readonly AgentApi api = new(factory);

    [Fact]
    public async Task ACustomerIs403OnTheCustomerTransactions()
    {
        (TestCustomer customer, Guid disputeId) = await api.SeedDisputeAsync();
        BrowserSession session = await BrowserSession.SignInAsync(factory, customer.User);

        Problem problem = await api.ProblemAsync(session.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId)), HttpStatusCode.Forbidden);

        Assert.Equal("forbidden", problem.Code);
    }

    [Fact]
    public async Task ADualRoleSessionIs403RoleConflictOnTheCustomerTransactions()
    {
        (TestCustomer customer, Guid disputeId) = await api.SeedDisputeAsync();
        BrowserSession both = await BrowserSession.SignInAsync(factory, customer.User with { Roles = [Roles.Customer, Roles.DisputeAgent] });

        Problem problem = await api.ProblemAsync(both.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId)), HttpStatusCode.Forbidden);

        Assert.Equal(SingleRoleRequirement.Code, problem.Code);
    }

    [Fact]
    public async Task AnUnauthenticatedCallerIs401OnTheCustomerTransactions()
    {
        (_, Guid disputeId) = await api.SeedDisputeAsync();
        using HttpClient browser = factory.CreateBrowserClient();
        using HttpRequestMessage request = new(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId));
        request.Headers.Add(CsrfMiddleware.HeaderName, "1");

        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AMissingDisputeIs404AndIsNotAudited()
    {
        (_, BrowserSession agent) = await api.NewAgentAsync();
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, "audit.events");

        Problem problem = await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(Guid.NewGuid())), HttpStatusCode.NotFound);

        Assert.Equal("dispute_not_found", problem.Code);
        Assert.Empty(probe.Samples);
    }

    // The list is for a dispute still under review: a decided or withdrawn one's id (the queue lists them) is a missing id,
    // so it cannot be used to read the customer's history, and nothing is audited.
    [Theory]
    [InlineData(DisputeStatus.Accepted)]
    [InlineData(DisputeStatus.Rejected)]
    [InlineData(DisputeStatus.Withdrawn)]
    public async Task ADisputeNoLongerUnderReviewIs404DisputeNotFoundAndIsNotAudited(DisputeStatus status)
    {
        (_, Guid disputeId, _) = await SeedAsync(status);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, "audit.events");

        Problem problem = await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId)), HttpStatusCode.NotFound);

        Assert.Equal("dispute_not_found", problem.Code);
        Assert.Empty(probe.Samples);
    }

    [Theory]
    [InlineData(DisputeStatus.Submitted)]
    [InlineData(DisputeStatus.UnderReview)]
    public async Task ADisputeUnderReviewIs200WithTheDisputedTransactionOnIt(DisputeStatus status)
    {
        (_, Guid disputeId, Guid disputed) = await SeedAsync(status);
        (_, BrowserSession agent) = await api.NewAgentAsync();

        JsonElement page = await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId));

        Assert.Equal([disputed], AgentApi.IdsOf(page.GetProperty("items").EnumerateArray()));
    }

    // Another customer's transactions are the newest of all: a list that lost its customer filter would put them first.
    [Fact]
    public async Task OnlyTheDisputedCustomersTransactionsAreListedNewestFirst()
    {
        (TestCustomer customer, Guid disputeId, Guid disputed) = await SeedAsync(DisputeStatus.Submitted);
        Guid yesterday = await AddPostedDebitAsync(customer, DaysAgo(1));
        Guid lastWeek = await AddPostedDebitAsync(customer, DaysAgo(7));
        Guid lastMonth = await AddPostedDebitAsync(customer, DaysAgo(30));
        TestCustomer other = await TestCustomer.CreateAsync(factory);
        await AddPostedDebitAsync(other, DateTimeOffset.UtcNow);
        await AddPostedDebitAsync(other, DaysAgo(2));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        JsonElement page = await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId, "?limit=100"));

        Assert.Equal([yesterday, disputed, lastWeek, lastMonth], AgentApi.IdsOf(page.GetProperty("items").EnumerateArray()));
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    // The window is whole UTC days, 90 back from today, and reaches back further only to take in the disputed transaction.
    [Fact]
    public async Task TheWindowIs90DaysUnlessTheDisputedTransactionIsOlder()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        Guid recentDisputed = await AddPostedDebitAsync(customer, DaysAgo(5));
        Guid recentDispute = await customer.AddDisputeAsync(factory, recentDisputed, DisputeStatus.Submitted);
        Guid inside = await AddPostedDebitAsync(customer, DaysAgo(89));
        Guid outside = await AddPostedDebitAsync(customer, DaysAgo(91));
        DateTimeOffset oldDate = DaysAgo(110);
        Guid oldDisputed = await AddPostedDebitAsync(customer, oldDate);
        Guid oldDispute = await customer.AddDisputeAsync(factory, oldDisputed, DisputeStatus.UnderReview);
        Guid olderStill = await AddPostedDebitAsync(customer, DaysAgo(130));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        DateOnly todayBefore = Today();
        JsonElement recentPage = await api.OkAsync(agent, AgentRequests.CustomerTransactions(recentDispute, "?limit=100"));
        DateOnly todayAfter = Today();
        JsonElement oldPage = await api.OkAsync(agent, AgentRequests.CustomerTransactions(oldDispute, "?limit=100"));

        Assert.Equal([recentDisputed, inside], AgentApi.IdsOf(recentPage.GetProperty("items").EnumerateArray()));
        Assert.Contains(DateOnly.Parse(recentPage.GetProperty("from").GetString()!), new[] { todayBefore.AddDays(-90), todayAfter.AddDays(-90) });
        // Everything from the disputed transaction's day on, so the 91-day-old one comes in with it; the 130-day-old one does not.
        Assert.Equal([recentDisputed, inside, outside, oldDisputed], AgentApi.IdsOf(oldPage.GetProperty("items").EnumerateArray()));
        Assert.DoesNotContain(olderStill, AgentApi.IdsOf(oldPage.GetProperty("items").EnumerateArray()));
        Assert.Equal(DateOnly.FromDateTime(oldDate.UtcDateTime), DateOnly.Parse(oldPage.GetProperty("from").GetString()!));
    }

    // The bound is midnight UTC of the day 90 back, inclusive: a row at that instant is in and the last second before it is
    // out. A bound on the instant 90 days ago, or a strict one, would drop the first.
    [Fact]
    public async Task TheWindowStartsAtMidnightUtcOfTheDayNinetyDaysBackAndIncludesIt()
    {
        (TestCustomer customer, Guid disputeId, Guid disputed) = await SeedAsync(DisputeStatus.Submitted);
        DateOnly startDay = Today().AddDays(-90);
        DateTimeOffset midnight = new(startDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        Guid atMidnight = await AddPostedDebitAsync(customer, midnight);
        await AddPostedDebitAsync(customer, midnight.AddSeconds(-1));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        JsonElement page = await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId, "?limit=100"));

        Assert.Equal([disputed, atMidnight], AgentApi.IdsOf(page.GetProperty("items").EnumerateArray()));
        Assert.Equal(startDay, DateOnly.Parse(page.GetProperty("from").GetString()!));
    }

    // Every field the page may carry, by name: a field added to the row must be added here on purpose.
    [Fact]
    public async Task ARowCarriesTheTransactionAndTheMaskedAccountAndNothingOfTheCustomer()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory, "Zanele Distinctive-Name");
        Guid disputed = await customer.AddEligibleTransactionAsync(factory);
        Guid disputeId = await customer.AddDisputeAsync(factory, disputed, DisputeStatus.UnderReview);
        await customer.AddTransactionAsync(factory, TransactionStatus.Pending, TransactionDirection.Credit, DaysAgo(1));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        HttpResponseMessage response = await api.SendAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId)));
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement body = JsonDocument.Parse(raw).RootElement;
        List<JsonElement> items = [.. body.GetProperty("items").EnumerateArray()];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["from", "items", "nextCursor"], body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(ItemFields, item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
        Assert.All(items, item => Assert.Equal("**** 0001", item.GetProperty("maskedAccountNumber").GetString()));
        Assert.Equal(["Pending", "Posted"], items.Select(item => item.GetProperty("status").GetString()).Order(StringComparer.Ordinal));
        JsonElement credit = items.Single(item => item.GetProperty("direction").GetString() == "Credit");
        Assert.Equal("Pending", credit.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, credit.GetProperty("postedAt").ValueKind);
        JsonElement debit = items.Single(item => item.GetProperty("direction").GetString() == "Debit");
        // As a string and exactly as the customer's own list serialises it.
        Assert.Equal(JsonValueKind.String, debit.GetProperty("amount").ValueKind);
        Assert.Equal("250.50", debit.GetProperty("amount").GetString());
        Assert.Equal("ZAR", debit.GetProperty("currency").GetString());
        Assert.Equal(disputed, debit.GetProperty("id").GetGuid());
        foreach (string leak in new[]
        {
            customer.User.Subject,
            customer.User.Name,
            customer.CustomerId.ToString("D"),
            customer.CustomerId.ToString("N"),
            customer.AccountId.ToString("D"),
            "isDisputable",
            "disputeBlockReasons",
        })
        {
            Assert.DoesNotContain(leak, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Active first, else the latest: the same answer the customer's own list gives for the transaction.
    [Fact]
    public async Task EachRowCarriesItsMostRelevantDisputeOrNone()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        Guid reopened = await AddPostedDebitAsync(customer, DaysAgo(1));
        await customer.AddDisputeAsync(factory, reopened, DisputeStatus.Withdrawn, DaysAgo(20));
        Guid reopenedDispute = await customer.AddDisputeAsync(factory, reopened, DisputeStatus.UnderReview, DaysAgo(10));
        Guid decided = await AddPostedDebitAsync(customer, DaysAgo(2));
        Guid decidedDispute = await customer.AddDisputeAsync(factory, decided, DisputeStatus.Rejected, DaysAgo(8));
        Guid withdrawnTwice = await AddPostedDebitAsync(customer, DaysAgo(3));
        await customer.AddDisputeAsync(factory, withdrawnTwice, DisputeStatus.Withdrawn, DaysAgo(30));
        Guid latestWithdrawn = await customer.AddDisputeAsync(factory, withdrawnTwice, DisputeStatus.Withdrawn, DaysAgo(15));
        Guid never = await AddPostedDebitAsync(customer, DaysAgo(4));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        JsonElement page = await api.OkAsync(agent, AgentRequests.CustomerTransactions(reopenedDispute));

        Dictionary<Guid, JsonElement> disputes = page.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetGuid(), item => item.GetProperty("dispute"));
        AssertDispute(disputes[reopened], reopenedDispute, "UnderReview");
        AssertDispute(disputes[decided], decidedDispute, "Rejected");
        AssertDispute(disputes[withdrawnTwice], latestWithdrawn, "Withdrawn");
        Assert.Equal(JsonValueKind.Null, disputes[never].ValueKind);
    }

    // Three rows share one instant (the id breaks the tie) and the pages are of 3: every row once, none skipped, in order.
    [Fact]
    public async Task CursorPagingRepeatsAndSkipsNothing()
    {
        (TestCustomer customer, Guid disputeId, Guid disputed) = await SeedAsync(DisputeStatus.Submitted);
        DateTimeOffset shared = DaysAgo(3);
        List<(Guid Id, DateTimeOffset Date)> rows =
        [
            (disputed, DaysAgo(5)),
            (await AddPostedDebitAsync(customer, DaysAgo(1)), DaysAgo(1)),
            (await AddPostedDebitAsync(customer, shared), shared),
            (await AddPostedDebitAsync(customer, shared), shared),
            (await AddPostedDebitAsync(customer, shared), shared),
            (await AddPostedDebitAsync(customer, DaysAgo(8)), DaysAgo(8)),
            (await AddPostedDebitAsync(customer, DaysAgo(9)), DaysAgo(9)),
        ];
        // The database orders uuids bytewise, which is their lower-case hex text.
        List<Guid> expected = [.. rows.OrderByDescending(row => row.Date).ThenByDescending(row => row.Id.ToString("D"), StringComparer.Ordinal).Select(row => row.Id)];
        (_, BrowserSession agent) = await api.NewAgentAsync();

        List<List<Guid>> pages = await WalkAsync(agent, disputeId, 3);

        Assert.Equal([3, 3, 1], pages.Select(page => page.Count));
        Assert.Equal(expected, [.. pages.SelectMany(page => page)]);
    }

    [Fact]
    public async Task ATamperedAnotherAgentsOrAnotherDisputesCursorIs400InvalidCursor()
    {
        (TestCustomer customer, Guid disputeId, _) = await SeedAsync(DisputeStatus.Submitted);
        await AddPostedDebitAsync(customer, DaysAgo(1));
        await AddPostedDebitAsync(customer, DaysAgo(2));
        // Another dispute of the same customer: the same rows, so only the cursor's binding can refuse it.
        Guid sameCustomerDispute = await customer.AddDisputeAsync(factory, await customer.AddEligibleTransactionAsync(factory), DisputeStatus.Submitted);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        (_, BrowserSession other) = await api.NewAgentAsync();
        string cursor = (await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId, "?limit=1"))).GetProperty("nextCursor").GetString()!;
        char[] tampered = cursor.ToCharArray();
        tampered[tampered.Length / 2] = tampered[tampered.Length / 2] == 'A' ? 'B' : 'A';

        Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId, $"?limit=1&cursor={Uri.EscapeDataString(cursor)}")))).StatusCode);
        await AssertInvalidCursorAsync(agent, disputeId, $"?limit=1&cursor={Uri.EscapeDataString(new string(tampered))}");
        await AssertInvalidCursorAsync(other, disputeId, $"?limit=1&cursor={Uri.EscapeDataString(cursor)}");
        await AssertInvalidCursorAsync(agent, sameCustomerDispute, $"?limit=1&cursor={Uri.EscapeDataString(cursor)}");
        await AssertInvalidCursorAsync(agent, disputeId, "?cursor=not-a-cursor");
        Assert.Equal(HttpStatusCode.OK, (await api.SendAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId, "?cursor=")))).StatusCode);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("limit=abc")]
    public async Task ABadLimitIs400WithTheFieldNamed(string query)
    {
        (_, Guid disputeId) = await api.SeedDisputeAsync();
        (_, BrowserSession agent) = await api.NewAgentAsync();

        Problem problem = await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId, "?" + query)), HttpStatusCode.BadRequest);

        Assert.Equal("validation_failed", problem.Code);
        Assert.True(problem.Body.GetProperty("errors").TryGetProperty("limit", out _), "errors.limit");
    }

    [Fact]
    public async Task APageHoldsAtMostTheLimitAndSaysWhenThereIsMore()
    {
        (TestCustomer customer, Guid disputeId, _) = await SeedAsync(DisputeStatus.Submitted);
        await AddPostedDebitAsync(customer, DaysAgo(1));
        (_, BrowserSession agent) = await api.NewAgentAsync();

        JsonElement page = await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId, "?limit=1"));

        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.NotNull(page.GetProperty("nextCursor").GetString());
    }

    private static DateTimeOffset DaysAgo(int days) => DateTimeOffset.UtcNow.AddDays(-days);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static void AssertDispute(JsonElement dispute, Guid id, string status)
    {
        Assert.Equal(["id", "status"], dispute.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(id, dispute.GetProperty("id").GetGuid());
        Assert.Equal(status, dispute.GetProperty("status").GetString());
    }

    private Task<Guid> AddPostedDebitAsync(TestCustomer customer, DateTimeOffset date) =>
        customer.AddTransactionAsync(factory, TransactionStatus.Posted, TransactionDirection.Debit, date);

    /// <summary>A new customer with an eligible transaction (five days old) and a dispute on it.</summary>
    private async Task<(TestCustomer Customer, Guid DisputeId, Guid TransactionId)> SeedAsync(DisputeStatus status)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        Guid transactionId = await customer.AddEligibleTransactionAsync(factory);
        return (customer, await customer.AddDisputeAsync(factory, transactionId, status), transactionId);
    }

    private async Task<List<List<Guid>>> WalkAsync(BrowserSession agent, Guid disputeId, int limit)
    {
        List<List<Guid>> pages = [];
        string? cursor = null;
        do
        {
            string query = $"?limit={limit}" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            JsonElement body = await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId, query));
            pages.Add(AgentApi.IdsOf(body.GetProperty("items").EnumerateArray()));
            cursor = body.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && pages.Count < 20);

        return pages;
    }

    private async Task AssertInvalidCursorAsync(BrowserSession session, Guid disputeId, string query)
    {
        Problem problem = await api.ProblemAsync(session.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId, query)), HttpStatusCode.BadRequest);

        Assert.Equal("invalid_cursor", problem.Code);
    }
}
