using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The races ADR-007 promises are safe, forced rather than timed: both requests pass every application check, then write
/// together, so only the database (the partial unique index, the primary key, xmin) can decide.
/// </summary>
[Collection("api")]
public sealed class DisputeConcurrencyTests(ApiFactory api)
{
    [Fact]
    public async Task TwoSubmitsForOneTransactionHeldTogetherGiveOne201AndOne409DisputeExists()
    {
        (TestCustomer customer, Guid transactionId, BrowserSession session) = await EligibleAsync();
        await using WebApplicationFactory<Program> gated = Gated(afterSubmissionFacts: new Rendezvous(2), afterFind: null);

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
        await using WebApplicationFactory<Program> gated = Gated(afterSubmissionFacts: new Rendezvous(2), afterFind: null);

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(gated, DisputeRequests.Submit(session, id, transactionId)),
            SendAsync(gated, DisputeRequests.Submit(session, id, transactionId)));

        HttpResponseMessage created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        HttpResponseMessage replayed = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replayed.Content.ReadAsStringAsync());
        Assert.Equal(1, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id)));
    }

    // Both withdrawals load version N and pass the If-Match check; the second UPDATE ... WHERE xmin = N finds nothing.
    [Fact]
    public async Task TwoWithdrawalsWithTheSameETagGiveOne200AndOne412WithTheNewETag()
    {
        (_, Guid transactionId, BrowserSession session) = await EligibleAsync();
        Guid id = Guid.NewGuid();
        HttpResponseMessage submitted = await SendAsync(api, DisputeRequests.Submit(session, id, transactionId));
        string etag = DisputeRequests.ETagOf(submitted)!;
        await using WebApplicationFactory<Program> gated = Gated(afterSubmissionFacts: null, afterFind: new Rendezvous(2));

        HttpResponseMessage[] responses = await Task.WhenAll(
            SendAsync(gated, DisputeRequests.Withdraw(session, id, etag)),
            SendAsync(gated, DisputeRequests.Withdraw(session, id, etag)));

        HttpResponseMessage won = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        HttpResponseMessage lost = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(DisputeRequests.ETagOf(won), DisputeRequests.ETagOf(lost));
        Assert.Equal("precondition_failed", (await Problem.ReadAsync(lost)).Code);
        Assert.Equal(2, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM dispute_status_changes WHERE dispute_id = @id", ("id", id)));
    }

    // EF retries a SaveChanges whose COMMIT succeeded but whose acknowledgement was lost; the id makes that retry a replay.
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

    private WebApplicationFactory<Program> Gated(Rendezvous? afterSubmissionFacts, Rendezvous? afterFind) =>
        api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<DisputeRepository>();
            services.AddScoped<IDisputeRepository>(provider =>
                new GatedDisputeRepository(provider.GetRequiredService<DisputeRepository>(), afterSubmissionFacts, afterFind));
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
