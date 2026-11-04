using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.IntegrationTests;

/// <summary>US2-US4 on the real stack: submit (idempotent), the 400/404/409/422 matrix, history, withdraw.</summary>
[Collection("api")]
public sealed class DisputesTests(ApiFactory api)
{
    [Fact]
    public async Task SubmittingCreatesADisputeWithLocationETagAndOneTimelineEntry()
    {
        (TestCustomer customer, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid id = Guid.NewGuid();

        HttpResponseMessage response = await SendAsync(DisputeRequests.Submit(session, id, transactionId, description: "  " + DisputeRequests.Description + "  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/disputes/{id}", response.Headers.Location?.OriginalString);
        Assert.Matches("^\"[0-9]+\"$", DisputeRequests.ETagOf(response));
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal(transactionId, body.GetProperty("transactionId").GetGuid());
        Assert.Equal(("Test merchant", "250.50", "ZAR"), (body.GetProperty("merchantName").GetString(), body.GetProperty("amount").GetString(), body.GetProperty("currency").GetString()));
        Assert.Equal((DisputeRequests.Reason, "Submitted"), (body.GetProperty("reasonCode").GetString(), body.GetProperty("status").GetString()));
        Assert.Equal(DisputeRequests.Description, body.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("resolutionNote").ValueKind);
        JsonElement entry = Assert.Single(body.GetProperty("timeline").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("from").ValueKind);
        Assert.Equal(("Submitted", "Customer"), (entry.GetProperty("to").GetString(), entry.GetProperty("actorType").GetString()));
        Assert.InRange(entry.GetProperty("occurredAt").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(transactionId, body.GetProperty("transaction").GetProperty("id").GetGuid());
        Assert.Equal("250.50", body.GetProperty("transaction").GetProperty("amount").GetString());
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM disputes WHERE customer_id = @customer", customer.CustomerId));
    }

    [Fact]
    public async Task OmittingTheAmountDisputesTheWholeTransactionAndAPartialAmountIsKept()
    {
        (TestCustomer customer, Guid whole, BrowserSession session) = await EligibleAsync();
        Guid partial = await customer.AddEligibleTransactionAsync(api);

        JsonElement wholeBody = await CreatedAsync(DisputeRequests.Submit(session, Guid.NewGuid(), whole, amount: null));
        JsonElement partialBody = await CreatedAsync(DisputeRequests.Submit(session, Guid.NewGuid(), partial, amount: "100.25"));

        Assert.Equal("250.50", wholeBody.GetProperty("amount").GetString());
        Assert.Equal("100.25", partialBody.GetProperty("amount").GetString());
        Assert.Equal("250.50", partialBody.GetProperty("transaction").GetProperty("amount").GetString());
    }

    [Fact]
    public async Task TheDisputeShowsInMyHistoryAndBlocksTheTransaction()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid id = Guid.NewGuid();
        await CreatedAsync(DisputeRequests.Submit(session, id, transactionId));

        JsonElement history = await OkAsync(session, DisputeRequests.Disputes);
        JsonElement transaction = await OkAsync(session, $"/api/v1/transactions/{transactionId}");

        JsonElement item = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal((id, transactionId), (item.GetProperty("id").GetGuid(), item.GetProperty("transactionId").GetGuid()));
        Assert.Equal(("Submitted", "250.50", "Test merchant"), (item.GetProperty("status").GetString(), item.GetProperty("amount").GetString(), item.GetProperty("merchantName").GetString()));
        Assert.Equal(id, transaction.GetProperty("dispute").GetProperty("id").GetGuid());
        Assert.Equal("Submitted", transaction.GetProperty("dispute").GetProperty("status").GetString());
        Assert.False(transaction.GetProperty("isDisputable").GetBoolean());
        Assert.Equal(["already_disputed"], transaction.GetProperty("disputeBlockReasons").EnumerateArray().Select(reason => reason.GetString()!));
    }

    [Fact]
    public async Task WithdrawingWithTheETagEndsItAndFreesTheTransactionForANewDispute()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid id = Guid.NewGuid();
        HttpResponseMessage created = await SendAsync(DisputeRequests.Submit(session, id, transactionId));
        string etag = DisputeRequests.ETagOf(created)!;

        HttpResponseMessage withdrawn = await SendAsync(DisputeRequests.Withdraw(session, id, etag));

        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.NotEqual(etag, DisputeRequests.ETagOf(withdrawn));
        JsonElement body = await withdrawn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Withdrawn", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(
            [(null, "Submitted", "Customer"), ("Submitted", "Withdrawn", "Customer")],
            body.GetProperty("timeline").EnumerateArray().Select(entry =>
                (entry.GetProperty("from").GetString(), entry.GetProperty("to").GetString(), entry.GetProperty("actorType").GetString())));
        JsonElement transaction = await OkAsync(session, $"/api/v1/transactions/{transactionId}");
        Assert.True(transaction.GetProperty("isDisputable").GetBoolean());
        Assert.Equal("Withdrawn", transaction.GetProperty("dispute").GetProperty("status").GetString());
        // A withdrawn dispute leaves the transaction open: a new dispute, with a new id, which the list then shows.
        Guid again = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(DisputeRequests.Submit(session, again, transactionId))).StatusCode);
        JsonElement redisputed = await OkAsync(session, $"/api/v1/transactions/{transactionId}");
        Assert.Equal((again, "Submitted"), (redisputed.GetProperty("dispute").GetProperty("id").GetGuid(), redisputed.GetProperty("dispute").GetProperty("status").GetString()));
        Assert.Equal(["already_disputed"], redisputed.GetProperty("disputeBlockReasons").EnumerateArray().Select(reason => reason.GetString()!));
    }

    [Fact]
    public async Task ReplayingTheSameIdReturnsTheOriginalWithoutASecondRow()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid id = Guid.NewGuid();
        HttpResponseMessage first = await SendAsync(DisputeRequests.Submit(session, id, transactionId));

        HttpResponseMessage replay = await SendAsync(DisputeRequests.Submit(session, id, transactionId));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(DisputeRequests.ETagOf(first), DisputeRequests.ETagOf(replay));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM disputes WHERE id = @id", id));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", id));
    }

    [Fact]
    public async Task ReusingAnIdForAnotherTransactionIs409IdempotencyKeyReuse()
    {
        (TestCustomer customer, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid other = await customer.AddEligibleTransactionAsync(api);
        Guid id = Guid.NewGuid();
        await CreatedAsync(DisputeRequests.Submit(session, id, transactionId));

        Problem problem = await ProblemAsync(DisputeRequests.Submit(session, id, other), HttpStatusCode.Conflict);

        Assert.Equal("idempotency_key_reuse", problem.Code);
        Assert.False(problem.Body.TryGetProperty("existingDisputeId", out _));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM disputes WHERE transaction_id = @id", other));
    }

    // The id is taken by someone else's dispute: a generic 409 that says nothing about that dispute.
    [Fact]
    public async Task AnotherCustomersDisputeIdIs409DisputeIdConflictWithoutLeaking()
    {
        (_, Guid theirTransaction, BrowserSession theirs) = await EligibleAsync();
        Guid id = Guid.NewGuid();
        await CreatedAsync(DisputeRequests.Submit(theirs, id, theirTransaction));
        (TestCustomer me, Guid myTransaction, BrowserSession mine) = await EligibleAsync();

        HttpResponseMessage response = await SendAsync(DisputeRequests.Submit(mine, id, myTransaction));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync();
        Assert.Equal("dispute_id_conflict", (await Problem.ReadAsync(response)).Code);
        Assert.DoesNotContain(theirTransaction.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("existingDisputeId", raw, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM disputes WHERE customer_id = @id", me.CustomerId));
    }

    [Theory]
    [InlineData("bad-id", "id", "validation_failed")]
    [InlineData("empty-id", "id", "validation_failed")]
    [InlineData("bad-transaction", "transactionId", "validation_failed")]
    [InlineData("unknown-reason", "reasonCode", "validation_failed")]
    [InlineData("short-description", "description", "validation_failed")]
    [InlineData("long-description", "description", "validation_failed")]
    [InlineData("three-decimals", "amount", "validation_failed")]
    [InlineData("negative-amount", "amount", "validation_failed")]
    [InlineData("card-number", "description", "sensitive_data")]
    [InlineData("sa-id-number", "description", "sensitive_data")]
    [InlineData("two-space-card", "description", "sensitive_data")]
    [InlineData("nul-description", "description", "invalid_characters")]
    [InlineData("nul-reason", "reasonCode", "invalid_characters")]
    public async Task InvalidBodiesAre400WithTheFieldNamed(string invalid, string field, string code)
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        string id = Guid.NewGuid().ToString();
        string tx = transactionId.ToString();
        object body = invalid switch
        {
            "bad-id" => new { id = "not-a-uuid", transactionId = tx, reasonCode = DisputeRequests.Reason, description = DisputeRequests.Description },
            "empty-id" => new { id = Guid.Empty.ToString(), transactionId = tx, reasonCode = DisputeRequests.Reason, description = DisputeRequests.Description },
            "bad-transaction" => new { id, transactionId = "", reasonCode = DisputeRequests.Reason, description = DisputeRequests.Description },
            "unknown-reason" => new { id, transactionId = tx, reasonCode = "made_up", description = DisputeRequests.Description },
            "short-description" => new { id, transactionId = tx, reasonCode = DisputeRequests.Reason, description = "  too short " },
            "long-description" => new { id, transactionId = tx, reasonCode = DisputeRequests.Reason, description = new string('d', 2001) },
            "three-decimals" => new { id, transactionId = tx, reasonCode = DisputeRequests.Reason, description = DisputeRequests.Description, amount = "12.345" },
            "negative-amount" => new { id, transactionId = tx, reasonCode = DisputeRequests.Reason, description = DisputeRequests.Description, amount = "-5.00" },
            "card-number" => new { id, transactionId = tx, reasonCode = DisputeRequests.Reason, description = "My card 4111 1111 1111 1111 was charged twice" },
            "two-space-card" => new { id, transactionId = tx, reasonCode = DisputeRequests.Reason, description = "paid with 4111  1111  1111  1111 twice" },
            "nul-description" => new { id, transactionId = tx, reasonCode = DisputeRequests.Reason, description = "Charged twice\u0000 for one order." },
            "nul-reason" => new { id, transactionId = tx, reasonCode = "other\u0000", description = DisputeRequests.Description },
            _ => new { id, transactionId = tx, reasonCode = DisputeRequests.Reason, description = "My ID number is 800101 5009 087, please check" },
        };

        Problem problem = await ProblemAsync(DisputeRequests.SubmitJson(session, JsonSerializer.Serialize(body)), HttpStatusCode.BadRequest);

        Assert.Equal(code, problem.Code);
        Assert.True(problem.Body.GetProperty("errors").TryGetProperty(field, out _), "errors." + field);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM disputes WHERE transaction_id = @id", transactionId));
    }

    // A lone surrogate cannot be stored as UTF-8; the JSON reader refuses it before anything reaches the database.
    [Fact]
    public async Task ALoneSurrogateIs400NotA500()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        string json = $$"""{"id":"{{Guid.NewGuid()}}","transactionId":"{{transactionId}}","reasonCode":"other","description":"Charged twice \ud800 for one order."}""";

        HttpResponseMessage response = await SendAsync(DisputeRequests.SubmitJson(session, json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await Problem.ReadAsync(response);
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM disputes WHERE transaction_id = @id", transactionId));
    }

    // Disputes created at the same instant are ordered by id: walking them across every page boundary loses and repeats nothing.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DisputesWithIdenticalCreatedAtArePagedWithoutDuplicatesOrGaps(int limit)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        DateTimeOffset tied = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        for (int index = 0; index < 4; index++)
        {
            await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), DisputeStatus.Withdrawn, tied);
        }

        await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), DisputeStatus.Submitted, tied.AddDays(-1));
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);
        List<Guid> expected = await AdminSql.ListAsync(api.AdminConnectionString,
            "SELECT id FROM disputes WHERE customer_id = @customer ORDER BY created_at DESC, id DESC", reader => reader.GetGuid(0), ("customer", customer.CustomerId));

        List<Guid> walked = [];
        string? cursor = null;
        do
        {
            JsonElement page = await OkAsync(session, $"{DisputeRequests.Disputes}?limit={limit}" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            walked.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(5, expected.Count);
        Assert.Equal(expected, walked);
    }

    // Unknown fields are refused, so a client typo ("ammount") surfaces instead of disputing the whole amount.
    [Fact]
    public async Task UnknownFieldsAre400()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        string json = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            transactionId,
            reasonCode = DisputeRequests.Reason,
            description = DisputeRequests.Description,
            ammount = "1.00",
        });

        Assert.Equal("malformed_request", (await ProblemAsync(DisputeRequests.SubmitJson(session, json), HttpStatusCode.BadRequest)).Code);
    }

    [Fact]
    public async Task AnotherCustomersTransactionIs404LikeAMissingOne()
    {
        (_, Guid theirs, _) = await EligibleAsync();
        (_, _, BrowserSession session) = await EligibleAsync();

        Problem foreign = await ProblemAsync(DisputeRequests.Submit(session, Guid.NewGuid(), theirs), HttpStatusCode.NotFound);
        Problem missing = await ProblemAsync(DisputeRequests.Submit(session, Guid.NewGuid(), Guid.NewGuid()), HttpStatusCode.NotFound);

        Assert.Equal((missing.Code, missing.Detail), (foreign.Code, foreign.Detail));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM disputes WHERE transaction_id = @id", theirs));
    }

    // An active, Accepted or Rejected dispute blocks a new one (409 with a link to it); a Withdrawn one does not.
    [Theory]
    [InlineData(DisputeStatus.Submitted)]
    [InlineData(DisputeStatus.UnderReview)]
    [InlineData(DisputeStatus.Accepted)]
    [InlineData(DisputeStatus.Rejected)]
    public async Task AnExistingDisputeIs409DisputeExistsWithItsId(DisputeStatus status)
    {
        (TestCustomer customer, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid existing = await customer.AddDisputeAsync(api, transactionId, status);

        Problem problem = await ProblemAsync(DisputeRequests.Submit(session, Guid.NewGuid(), transactionId), HttpStatusCode.Conflict);

        Assert.Equal("dispute_exists", problem.Code);
        Assert.Equal(existing, problem.Body.GetProperty("existingDisputeId").GetGuid());
    }

    [Fact]
    public async Task EveryBrokenRuleIsReportedAtOnceAs422()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid pendingOldCredit = await customer.AddTransactionAsync(api, TransactionStatus.Pending, TransactionDirection.Credit, DateTimeOffset.UtcNow.AddDays(-130));
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);

        Problem problem = await ProblemAsync(DisputeRequests.Submit(session, Guid.NewGuid(), pendingOldCredit, amount: "999.00"), HttpStatusCode.UnprocessableEntity);

        Assert.Equal("eligibility_failed", problem.Code);
        Assert.Equal(["not_posted", "not_debit", "window_expired", "amount_exceeds_transaction"], Violations(problem));
        Assert.All(problem.Body.GetProperty("violations").EnumerateArray(), violation =>
            Assert.False(string.IsNullOrWhiteSpace(violation.GetProperty("message").GetString())));
    }

    [Fact]
    public async Task AZeroAmountIs422AmountNotPositive()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();

        Problem problem = await ProblemAsync(DisputeRequests.Submit(session, Guid.NewGuid(), transactionId, amount: "0.00"), HttpStatusCode.UnprocessableEntity);

        Assert.Equal(["amount_not_positive"], Violations(problem));
    }

    [Fact]
    public async Task The21stOpenDisputeIs422TooManyActiveDisputes()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        for (int index = 0; index < 20; index++)
        {
            await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), index % 2 == 0 ? DisputeStatus.Submitted : DisputeStatus.UnderReview);
        }

        Guid twentyFirst = await customer.AddEligibleTransactionAsync(api);
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);

        Problem problem = await ProblemAsync(DisputeRequests.Submit(session, Guid.NewGuid(), twentyFirst), HttpStatusCode.UnprocessableEntity);

        Assert.Equal(["too_many_active_disputes"], Violations(problem));
    }

    [Fact]
    public async Task AFourthDisputeOnOneTransactionIs422AndTheListSaysSo()
    {
        (TestCustomer customer, Guid transactionId, BrowserSession session) = await EligibleAsync();
        for (int index = 0; index < 3; index++)
        {
            await customer.AddDisputeAsync(api, transactionId, DisputeStatus.Withdrawn);
        }

        Problem problem = await ProblemAsync(DisputeRequests.Submit(session, Guid.NewGuid(), transactionId), HttpStatusCode.UnprocessableEntity);
        JsonElement transaction = await OkAsync(session, $"/api/v1/transactions/{transactionId}");

        Assert.Equal(["too_many_disputes_for_transaction"], Violations(problem));
        Assert.False(transaction.GetProperty("isDisputable").GetBoolean());
        Assert.Equal(["too_many_disputes_for_transaction"], transaction.GetProperty("disputeBlockReasons").EnumerateArray().Select(reason => reason.GetString()!));
    }

    [Fact]
    public async Task TheReasonCatalogueListsSevenReasonsInDisplayOrder()
    {
        (_, _, BrowserSession session) = await EligibleAsync();

        JsonElement reasons = await OkAsync(session, DisputeRequests.Reasons);

        Assert.Equal(
            ["unrecognised_transaction", "duplicate_charge", "incorrect_amount", "goods_not_received", "subscription_cancelled", "card_lost_or_stolen", "other"],
            reasons.EnumerateArray().Select(reason => reason.GetProperty("code").GetString()!));
        Assert.All(reasons.EnumerateArray(), reason =>
        {
            Assert.False(string.IsNullOrWhiteSpace(reason.GetProperty("label").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(reason.GetProperty("description").GetString()));
        });
    }

    // Every status, newest first, keyset-paged with no duplicate or gap; the status filter narrows it.
    [Fact]
    public async Task TheHistoryWalksEveryStatusNewestFirstAndFiltersByStatus()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        foreach (DisputeStatus status in new[] { DisputeStatus.Withdrawn, DisputeStatus.Accepted, DisputeStatus.Submitted })
        {
            await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), status);
        }

        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);
        List<Guid> expected = await AdminSql.ListAsync(api.AdminConnectionString,
            "SELECT id FROM disputes WHERE customer_id = @customer ORDER BY created_at DESC, id DESC", reader => reader.GetGuid(0), ("customer", customer.CustomerId));

        List<Guid> walked = [];
        string? cursor = null;
        do
        {
            JsonElement page = await OkAsync(session, DisputeRequests.Disputes + "?limit=1" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            walked.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        JsonElement accepted = await OkAsync(session, DisputeRequests.Disputes + "?status=accepted");
        Assert.Equal(3, expected.Count);
        Assert.Equal(expected, walked);
        Assert.Equal(["Accepted"], accepted.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString()!));
    }

    [Theory]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=abc", "limit")]
    [InlineData("?status=Open", "status")]
    public async Task OutOfBoundsHistoryFiltersAre400WithTheFieldNamed(string query, string field)
    {
        (_, _, BrowserSession session) = await EligibleAsync();

        Problem problem = await ProblemAsync(session.Request(HttpMethod.Get, DisputeRequests.Disputes + query), HttpStatusCode.BadRequest);

        Assert.Equal("validation_failed", problem.Code);
        Assert.True(problem.Body.GetProperty("errors").TryGetProperty(field, out _), "errors." + field);
    }

    private static string[] Violations(Problem problem) =>
        [.. problem.Body.GetProperty("violations").EnumerateArray().Select(violation => violation.GetProperty("code").GetString()!)];

    private async Task<(TestCustomer Customer, Guid TransactionId, BrowserSession Session)> EligibleAsync()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid transactionId = await customer.AddEligibleTransactionAsync(api);
        return (customer, transactionId, await BrowserSession.SignInAsync(api, customer.User));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using HttpClient browser = api.CreateBrowserClient();
        return await browser.SendAsync(request);
    }

    private async Task<JsonElement> CreatedAsync(HttpRequestMessage request)
    {
        HttpResponseMessage response = await SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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

    private Task<long> CountAsync(string sql, Guid id) =>
        AdminSql.CountAsync(api.AdminConnectionString, sql, (sql.Contains("@customer", StringComparison.Ordinal) ? "customer" : "id", id));
}
