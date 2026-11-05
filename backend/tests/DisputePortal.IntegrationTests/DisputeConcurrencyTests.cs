using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The races ADR-007 and ADR-014 promise are safe, forced rather than timed. Backstop races switch the customer lock off,
/// so both requests pass every application check, then write together, and only the database (the partial unique index,
/// the primary key, xmin) can decide. Lock races keep it on and gate each racer until the others are queued behind it.
/// </summary>
[Collection("api")]
public sealed class DisputeConcurrencyTests(ApiFactory api)
{
    private const int Racers = 8;

    [Fact]
    public async Task TwoSubmitsForOneTransactionHeldTogetherGiveOne201AndOne409DisputeExists()
    {
        (TestCustomer customer, Guid transactionId, BrowserSession session) = await EligibleAsync();
        await using WebApplicationFactory<Program> gated = Gated(afterSubmissionFacts: new Rendezvous(2).ArriveAsync, afterFind: null, customerLock: false);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(gated, DisputeRequests.Submit(session, Guid.NewGuid(), transactionId)),
            SendAsync(gated, DisputeRequests.Submit(session, Guid.NewGuid(), transactionId)));

        HttpResponseMessage winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        HttpResponseMessage loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Guid winnerId = (await winner.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Problem problem = await Problem.ReadAsync(loser);
        Assert.Equal("dispute_exists", problem.Code);
        Assert.Equal(winnerId, problem.Body.GetProperty("existingDisputeId").GetGuid());
        Assert.Equal(1, await AdminSql.CountAsync(api.AdminConnectionString,
            "SELECT count(*) FROM disputes WHERE customer_id = @customer", ("customer", customer.CustomerId)));
    }

    // A double click: the same submission twice at once. One creates it; the other collides on the key and replays it.
    [Fact]
    public async Task TheSameSubmissionRacingItselfGivesOne201AndOne200WithTheSameBody()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid id = Guid.NewGuid();
        await using WebApplicationFactory<Program> gated = Gated(afterSubmissionFacts: new Rendezvous(2).ArriveAsync, afterFind: null, customerLock: false);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(gated, DisputeRequests.Submit(session, id, transactionId)),
            SendAsync(gated, DisputeRequests.Submit(session, id, transactionId)));

        await AssertOneCreatedAndOneReplayedAsync(responses, id);
    }

    // The double click under the lock: the second waits for the first, then finds its id inside the lock and replays it.
    // A replay check before the lock would find nothing, then see the first's dispute as an existing one (409).
    [Fact]
    public async Task TheSameSubmissionRacingItselfThroughTheLockGivesOne201AndOne200WithTheSameBody()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid id = Guid.NewGuid();
        await using WebApplicationFactory<Program> gated = Gated(
            afterSubmissionFacts: new AdvisoryLockRendezvous(2, api.AdminConnectionString).ArriveAsync, afterFind: null, customerLock: true);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(gated, DisputeRequests.Submit(session, id, transactionId)),
            SendAsync(gated, DisputeRequests.Submit(session, id, transactionId)));

        await AssertOneCreatedAndOneReplayedAsync(responses, id);
    }

    // One open dispute short of the cap, eight submits at once on eight transactions: each racer reads the count only
    // while it holds the customer's lock, so one gets the last place and the other seven see the cap. Without the lock
    // all eight would read 19 and all eight would be created.
    [Fact]
    public async Task ParallelSubmitsOneBelowTheActiveCapGiveOne201AndTheRest422TooManyActiveDisputes()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        for (int index = 0; index < 19; index++)
        {
            await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), DisputeStatus.Submitted);
        }

        Guid[] transactions = await Task.WhenAll(Enumerable.Range(0, Racers).Select(_ => customer.AddEligibleTransactionAsync(api)));
        BrowserSession session = await BrowserSession.SignInAsync(api, customer.User);
        await using WebApplicationFactory<Program> gated = Gated(
            afterSubmissionFacts: new AdvisoryLockRendezvous(Racers, api.AdminConnectionString).ArriveAsync, afterFind: null, customerLock: true);

        HttpResponseMessage[] responses = await Task.WhenAll(
            transactions.Select(transactionId => SendAsync(gated, DisputeRequests.Submit(session, Guid.NewGuid(), transactionId))));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        HttpResponseMessage[] refused = [.. responses.Where(response => response.StatusCode != HttpStatusCode.Created)];
        Assert.Equal(Racers - 1, refused.Length);
        foreach (HttpResponseMessage response in refused)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal("eligibility_failed", problem.Code);
            Assert.Equal(["too_many_active_disputes"], problem.Body.GetProperty("violations").EnumerateArray().Select(violation => violation.GetProperty("code").GetString()!));
        }

        Assert.Equal(20, await AdminSql.CountAsync(api.AdminConnectionString,
            "SELECT count(*) FROM disputes WHERE customer_id = @customer AND status IN ('Submitted', 'UnderReview')", ("customer", customer.CustomerId)));
    }

    // Both withdrawals load version N and pass the If-Match check; the second UPDATE ... WHERE xmin = N finds nothing.
    [Fact]
    public async Task TwoWithdrawalsWithTheSameETagGiveOne200AndOne412WithTheNewETag()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid id = Guid.NewGuid();
        HttpResponseMessage submitted = await SendAsync(api, DisputeRequests.Submit(session, id, transactionId));
        string etag = DisputeRequests.ETagOf(submitted)!;
        await using WebApplicationFactory<Program> gated = Gated(afterSubmissionFacts: null, afterFind: new Rendezvous(2).ArriveAsync, customerLock: true);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(gated, DisputeRequests.Withdraw(session, id, etag)),
            SendAsync(gated, DisputeRequests.Withdraw(session, id, etag)));

        HttpResponseMessage won = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        HttpResponseMessage lost = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(DisputeRequests.ETagOf(won), DisputeRequests.ETagOf(lost));
        Assert.Equal("precondition_failed", (await Problem.ReadAsync(lost)).Code);
        Assert.Equal(2, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id)));
    }

    // EF re-runs the submit transaction whose COMMIT succeeded but whose acknowledgement was lost; the id, checked again
    // inside the lock, makes that retry a replay. The real lock: one request never waits on it.
    [Fact]
    public async Task AnEfRetryAfterALostCommitAcknowledgementReplaysInsteadOfInsertingTwice()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        LostCommitAcknowledgement lostAck = new();
        await using WebApplicationFactory<Program> flaky = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<DisputePortalDbContext>(options => options.AddInterceptors(lostAck))));
        Guid id = Guid.NewGuid();
        await SendAsync(flaky, session.Request(HttpMethod.Get, DisputeRequests.Reasons));
        lostAck.Arm();

        HttpResponseMessage response = await SendAsync(flaky, DisputeRequests.Submit(session, id, transactionId));

        Assert.Equal(1, lostAck.Fired);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(1, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM disputes WHERE id = @id", ("id", id)));
        Assert.Equal(1, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id)));
    }

    // A transient failure before the COMMIT: the strategy runs the whole attempt again (lock, reads, insert) from a clean
    // change tracker, so the retry neither trips over the failed attempt's entities nor turns into a 503.
    [Fact]
    public async Task ATransientFailureBeforeTheCommitRunsTheWholeSubmitAgainAndCreatesTheDisputeOnce()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        TransientInsertFailure failure = new();
        await using WebApplicationFactory<Program> flaky = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<DisputePortalDbContext>(options => options.AddInterceptors(failure))));
        Guid id = Guid.NewGuid();
        failure.Arm();

        HttpResponseMessage response = await SendAsync(flaky, DisputeRequests.Submit(session, id, transactionId));

        Assert.Equal(1, failure.Fired);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id)));
    }

    // Outside a transaction pg_advisory_xact_lock is released when its statement ends: refused rather than silently useless.
    [Fact]
    public async Task TheCustomerLockRefusesToRunOutsideATransaction()
    {
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        ICustomerLock customerLock = scope.ServiceProvider.GetRequiredService<ICustomerLock>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => customerLock.AcquireAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private async Task AssertOneCreatedAndOneReplayedAsync(HttpResponseMessage[] responses, Guid id)
    {
        HttpResponseMessage created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        HttpResponseMessage replayed = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replayed.Content.ReadAsStringAsync());
        Assert.Equal(1, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id)));
    }

    /// <param name="customerLock">False for the backstop races: <see cref="NoCustomerLock"/> lets both racers into the window.</param>
    [Fact]
    public async Task TwoDifferentCustomersHeldTogetherInsideTheirLocksDoNotBlockEachOther()
    {
        (_, Guid firstTransaction, BrowserSession first) = await EligibleAsync();
        (_, Guid secondTransaction, BrowserSession second) = await EligibleAsync();
        // A plain rendezvous: each holder waits, inside its own lock, for the other to be inside its lock too. A lock that
        // was not per customer would deadlock it until the rendezvous timeout, and the test would fail.
        await using WebApplicationFactory<Program> gated = Gated(afterSubmissionFacts: new Rendezvous(2).ArriveAsync, afterFind: null, customerLock: true);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(gated, DisputeRequests.Submit(first, Guid.NewGuid(), firstTransaction)),
            SendAsync(gated, DisputeRequests.Submit(second, Guid.NewGuid(), secondTransaction)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
    }

    [Fact]
    public async Task ACancelledWaitForTheCustomerLockEndsPromptlyAndLeavesNoQueuedLock()
    {
        Guid customerId = Guid.NewGuid();
        await using NpgsqlConnection holder = new(api.AdminConnectionString);
        await using NpgsqlTransaction held = await HoldCustomerLockAsync(holder, customerId);
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();
        ICustomerLock customerLock = scope.ServiceProvider.GetRequiredService<ICustomerLock>();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => customerLock.AcquireAsync(customerId, cancel.Token));
        await transaction.RollbackAsync();

        Assert.Equal(0, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted"));
    }

    // Production connections carry statement_timeout=5000, which cancels a queued lock wait. The test uses a short one so
    // it runs in a moment: the cancelled wait must surface as the 503 the API documents, not as a 500 internal_error.
    [Fact]
    public async Task AWaitForTheCustomerLockPastTheStatementTimeoutIsADatabaseUnavailable503()
    {
        Guid customerId = Guid.NewGuid();
        await using NpgsqlConnection holder = new(api.AdminConnectionString);
        await using NpgsqlTransaction held = await HoldCustomerLockAsync(holder, customerId);
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();
        ICustomerLock customerLock = scope.ServiceProvider.GetRequiredService<ICustomerLock>();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '300ms'");

        DatabaseUnavailableException refusal = await Assert.ThrowsAsync<DatabaseUnavailableException>(
            () => customerLock.AcquireAsync(customerId, CancellationToken.None));

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, refusal.Status);
    }

    /// <summary>Takes the customer's advisory lock on a separate admin connection, so a request for that customer has to wait.</summary>
    private static async Task<NpgsqlTransaction> HoldCustomerLockAsync(NpgsqlConnection holder, Guid customerId)
    {
        await holder.OpenAsync();
        NpgsqlTransaction held = await holder.BeginTransactionAsync();
        await using NpgsqlCommand take = new("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", holder, held);
        take.Parameters.AddWithValue("key", customerId.ToString("D"));
        await take.ExecuteNonQueryAsync();
        return held;
    }

    private WebApplicationFactory<Program> Gated(Func<CancellationToken, Task>? afterSubmissionFacts, Func<CancellationToken, Task>? afterFind, bool customerLock) =>
        api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<DisputeRepository>();
            services.AddScoped<IDisputeRepository>(provider =>
                new GatedDisputeRepository(provider.GetRequiredService<DisputeRepository>(), afterSubmissionFacts, afterFind));
            if (!customerLock)
            {
                services.AddScoped<ICustomerLock, NoCustomerLock>();
            }
        }));

    private async Task<(TestCustomer Customer, Guid TransactionId, BrowserSession Session)> EligibleAsync()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid transactionId = await customer.AddEligibleTransactionAsync(api);
        return (customer, transactionId, await BrowserSession.SignInAsync(api, customer.User));
    }

    private static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> host, HttpRequestMessage request)
    {
        using HttpClient browser = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri(ApiFactory.PublicBaseUrl),
        });
        return await browser.SendAsync(request);
    }
}
