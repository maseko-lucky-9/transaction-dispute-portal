using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.IntegrationTests;

/// <summary>US1 / FR-1 on the real stack: keyset paging, filters, protected cursors and ownership (BOLA).</summary>
[Collection("api")]
public sealed partial class TransactionsTests(ApiFactory api)
{
    private const string List = "/api/v1/transactions";

    private const string CustomerIdsByKeysetSql =
        "SELECT id FROM transactions WHERE customer_id = @customer ORDER BY transaction_date DESC, id DESC";

    private static IdpUser Thandi() => new(Seeder.ThandiSubject, "Thandi Nkosi", [Roles.Customer]);

    private static IdpUser Sipho() => new(Seeder.SiphoSubject, "Sipho Dlamini", [Roles.Customer]);

    [Fact]
    public async Task WalkingEveryPageReturnsAllOfMyTransactionsInKeysetOrderWithNoDuplicatesOrGaps()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        List<Guid> expected = await AdminSql.ListAsync(api.AdminConnectionString, CustomerIdsByKeysetSql, reader => reader.GetGuid(0), ("customer", Seeder.ThandiCustomerId));

        List<Guid> walked = [];
        int pages = 0;
        string? cursor = null;
        do
        {
            JsonElement page = await OkAsync(session, List + "?limit=10" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            walked.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(75, expected.Count);
        Assert.Equal(expected, walked);
        Assert.Equal(8, pages);
    }

    [Fact]
    public async Task TheDefaultPageIs25AndTheLastPageHasNoCursor()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());

        JsonElement first = await OkAsync(session, List);
        JsonElement whole = await OkAsync(session, List + "?limit=100");

        Assert.Equal(25, first.GetProperty("items").GetArrayLength());
        Assert.NotNull(first.GetProperty("nextCursor").GetString());
        Assert.Equal(75, whole.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, whole.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?from=2026-13-01", "from")]
    [InlineData("?to=01-02-2026", "to")]
    [InlineData("?from=2026-03-02&to=2026-03-01", "to")]
    [InlineData("?from=2025-01-01&to=2026-01-03", "to")]
    [InlineData("?status=Settled", "status")]
    [InlineData("?limit=abc", "limit")]
    [InlineData("?limit=1e2", "limit")]
    [InlineData("?limit=99999999999", "limit")]
    // ToExclusive adds a day: the last representable date used to throw (500 internal_error).
    [InlineData("?to=9999-12-31", "to")]
    [InlineData("?from=9999-12-31&to=9999-12-31", "from")]
    [InlineData("?from=9999-12-31&to=9999-12-31", "to")]
    [InlineData("?from=0001-01-01", "from")]
    [InlineData("?from=1899-12-31", "from")]
    [InlineData("?to=2101-01-01", "to")]
    public async Task OutOfBoundsFiltersAre400WithTheFieldNamed(string query, string field)
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, List + query));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Problem problem = await Problem.ReadAsync(response);
        Assert.Equal("validation_failed", problem.Code);
        Assert.True(problem.Body.GetProperty("errors").TryGetProperty(field, out _), "errors." + field);
    }

    // A form that serialises an empty field must not be told its cursor is invalid: empty means the first page.
    [Fact]
    public async Task AnEmptyCursorIsTheFirstPage()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());

        JsonElement first = await OkAsync(session, List);
        JsonElement empty = await OkAsync(session, List + "?cursor=");

        Assert.Equal(25, empty.GetProperty("items").GetArrayLength());
        Assert.Equal(first.GetProperty("items").GetRawText(), empty.GetProperty("items").GetRawText());
    }

    // Ties on transaction_date are broken by id: walking identical dates across every page boundary loses and repeats nothing.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RowsWithIdenticalDatesArePagedWithoutDuplicatesOrGaps(int limit)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        DateTimeOffset tied = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        for (int index = 0; index < 5; index++)
        {
            await customer.AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Debit, tied);
        }

        await customer.AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Debit, tied.AddDays(-1));
        List<Guid> expected = await AdminSql.ListAsync(api.AdminConnectionString, CustomerIdsByKeysetSql, reader => reader.GetGuid(0), ("customer", customer.CustomerId));
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);

        List<Guid> walked = [];
        string? cursor = null;
        do
        {
            JsonElement page = await OkAsync(session, $"{List}?limit={limit}" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            walked.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(6, expected.Count);
        Assert.Equal(expected, walked);
    }

    [Fact]
    public async Task TheStatusFilterReturnsOnlyThatStatus()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        long pending = await AdminSql.CountAsync(api.AdminConnectionString,
            "SELECT count(*) FROM transactions WHERE customer_id = @customer AND status = 'Pending'", ("customer", Seeder.ThandiCustomerId));

        JsonElement page = await OkAsync(session, List + "?status=pending&limit=100");

        Assert.Equal(pending, page.GetProperty("items").GetArrayLength());
        Assert.All(page.GetProperty("items").EnumerateArray(), item => Assert.Equal("Pending", item.GetProperty("status").GetString()));
    }

    // Fixed instants, not the clock: from and to are whole UTC days, both inclusive, to the millisecond.
    [Fact]
    public async Task TheDateFilterKeepsWholeUtcDaysInclusive()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        TimeSpan utc = TimeSpan.Zero;
        Guid firstMomentOfFrom = await customer.AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Debit, new(2026, 3, 1, 0, 0, 0, utc));
        Guid lastMomentOfTo = await customer.AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Debit, new(2026, 3, 31, 23, 59, 59, 999, utc));
        await customer.AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Debit, new(2026, 4, 1, 0, 0, 0, utc));
        await customer.AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Debit, new(2026, 2, 28, 23, 59, 59, 999, utc));
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);

        JsonElement page = await OkAsync(session, List + "?from=2026-03-01&to=2026-03-31");

        Assert.Equal(
            [lastMomentOfTo, firstMomentOfFrom],
            page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray());
    }

    [Fact]
    public async Task ATamperedCursorIs400InvalidCursor()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        char[] cursor = (await OkAsync(session, List + "?limit=5")).GetProperty("nextCursor").GetString()!.ToCharArray();
        cursor[cursor.Length / 2] = cursor[cursor.Length / 2] == 'A' ? 'B' : 'A';

        await AssertInvalidCursorAsync(session, List + "?limit=5&cursor=" + Uri.EscapeDataString(new string(cursor)));
    }

    [Fact]
    public async Task AnotherCustomersCursorIs400InvalidCursor()
    {
        BrowserSession thandi = await BrowserSession.SignInAsync(api, Thandi());
        BrowserSession sipho = await BrowserSession.SignInAsync(api, Sipho());
        string thandisCursor = (await OkAsync(thandi, List + "?limit=5")).GetProperty("nextCursor").GetString()!;

        await AssertInvalidCursorAsync(sipho, List + "?limit=5&cursor=" + Uri.EscapeDataString(thandisCursor));
    }

    [Fact]
    public async Task ACursorReusedWithOtherFiltersIs400InvalidCursor()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        string postedCursor = (await OkAsync(session, List + "?status=Posted&limit=5")).GetProperty("nextCursor").GetString()!;

        await AssertInvalidCursorAsync(session, List + "?status=Pending&limit=5&cursor=" + Uri.EscapeDataString(postedCursor));
    }

    // BOLA: another customer's transaction looks exactly like one that does not exist.
    [Fact]
    public async Task AnotherCustomersTransactionIs404LikeAMissingOne()
    {
        BrowserSession thandi = await BrowserSession.SignInAsync(api, Thandi());
        Guid siphosTransaction = (await AdminSql.ListAsync(api.AdminConnectionString, CustomerIdsByKeysetSql, reader => reader.GetGuid(0), ("customer", Seeder.SiphoCustomerId)))[0];
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage foreign = await browser.SendAsync(thandi.Request(HttpMethod.Get, $"{List}/{siphosTransaction}"));
        HttpResponseMessage missing = await browser.SendAsync(thandi.Request(HttpMethod.Get, $"{List}/{Guid.NewGuid()}"));

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Problem foreignProblem = await Problem.ReadAsync(foreign);
        Problem missingProblem = await Problem.ReadAsync(missing);
        Assert.Equal((missingProblem.Code, missingProblem.Detail), (foreignProblem.Code, foreignProblem.Detail));
        Assert.Equal("transaction_not_found", foreignProblem.Code);
    }

    [Fact]
    public async Task AnotherCustomersTransactionsNeverAppearInMyList()
    {
        BrowserSession sipho = await BrowserSession.SignInAsync(api, Sipho());
        HashSet<Guid> thandis = [.. await AdminSql.ListAsync(api.AdminConnectionString, CustomerIdsByKeysetSql, reader => reader.GetGuid(0), ("customer", Seeder.ThandiCustomerId))];

        JsonElement page = await OkAsync(sipho, List + "?limit=100");

        Assert.Equal(75, page.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), item => thandis.Contains(item.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task AMalformedIdIs404NotA500()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, List + "/not-a-guid"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(List)]
    [InlineData(List + "/c0570000-0000-4000-8000-000000000001")]
    public async Task AnAgentIs403OnEveryCustomerEndpoint(string path)
    {
        BrowserSession agent = await BrowserSession.SignInAsync(api, new IdpUser(Seeder.AgentSubject, "Agent", [Roles.DisputeAgent]));
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(agent.Request(HttpMethod.Get, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", (await Problem.ReadAsync(response)).Code);
    }

    [Theory]
    [InlineData(List)]
    [InlineData(List + "/c0570000-0000-4000-8000-000000000001")]
    public async Task ADualRoleSessionIs403RoleConflictOnEveryCustomerEndpoint(string path)
    {
        BrowserSession both = await BrowserSession.SignInAsync(api, new IdpUser(Seeder.ThandiSubject, "Thandi Nkosi", [Roles.Customer, Roles.DisputeAgent]));
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(both.Request(HttpMethod.Get, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SingleRoleRequirement.Code, (await Problem.ReadAsync(response)).Code);
    }

    [Theory]
    [InlineData(List)]
    [InlineData(List + "/c0570000-0000-4000-8000-000000000001")]
    public async Task AnAnonymousCallerIs401OnEveryCustomerEndpoint(string path)
    {
        using HttpClient browser = api.CreateBrowserClient();
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Add(CsrfMiddleware.HeaderName, "1");

        HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ACustomerRoleWithoutACustomerRecordIs403NotProvisioned()
    {
        BrowserSession stranger = await BrowserSession.SignInAsync(api, new IdpUser("unknown-" + Guid.NewGuid().ToString("N"), "Stranger", [Roles.Customer]));
        using HttpClient browser = api.CreateBrowserClient();

        HttpResponseMessage response = await browser.SendAsync(stranger.Request(HttpMethod.Get, List));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CustomerScopeFilter.NotProvisionedCode, (await Problem.ReadAsync(response)).Code);
    }

    [Fact]
    public async Task EachItemHasTheContractShape()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, Thandi());

        JsonElement page = await OkAsync(session, List + "?limit=100");
        Guid firstId = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        JsonElement detail = await OkAsync(session, $"{List}/{firstId}");

        Assert.Equal(page.GetProperty("items")[0].GetRawText(), detail.GetRawText());
        Assert.All(page.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Matches(AmountPattern(), item.GetProperty("amount").GetString());
            Assert.Equal("ZAR", item.GetProperty("currency").GetString());
            Assert.Matches(MaskedNumberPattern(), item.GetProperty("account").GetProperty("maskedNumber").GetString());
            Assert.Contains(item.GetProperty("direction").GetString(), new[] { "Debit", "Credit" });
            Assert.Equal(JsonValueKind.Null, item.GetProperty("dispute").ValueKind);
            Assert.Equal(item.GetProperty("isDisputable").GetBoolean(), item.GetProperty("disputeBlockReasons").GetArrayLength() == 0);
            Assert.Equal(item.GetProperty("status").GetString() == "Pending", item.GetProperty("postedAt").ValueKind == JsonValueKind.Null);
        });
    }

    private async Task<JsonElement> OkAsync(BrowserSession session, string path)
    {
        using HttpClient browser = api.CreateBrowserClient();
        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task AssertInvalidCursorAsync(BrowserSession session, string path)
    {
        using HttpClient browser = api.CreateBrowserClient();
        HttpResponseMessage response = await browser.SendAsync(session.Request(HttpMethod.Get, path));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_cursor", (await Problem.ReadAsync(response)).Code);
    }

    [GeneratedRegex(@"^\d+\.\d{2,4}$")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^\*{4} \d{4}$")]
    private static partial Regex MaskedNumberPattern();
}
