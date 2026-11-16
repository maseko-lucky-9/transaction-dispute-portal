using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The list shows a customer's merchants, amounts and masked account number, so loading a page is audited like opening a
/// dispute: <c>audit.events{action=agent_customer_transactions_viewed}</c> and one <c>agent.customer_transactions.viewed</c>
/// log line, once per page.
/// </summary>
[Collection("api")]
public sealed class AgentCustomerTransactionsAuditTests(ApiFactory factory)
{
    private const string AuditEvents = "audit.events";
    private const string ViewedAction = "agent_customer_transactions_viewed";

    private readonly AgentApi api = new(factory);

    [Fact]
    public async Task EveryPageIsOneAuditEventWhateverItsRowCount()
    {
        (_, Guid disputeId) = await SeedWithRowsAsync(2);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, AuditEvents);
        List<int> rowCounts = [];

        foreach (string query in new[] { "?limit=1", "?limit=2", "?limit=100", string.Empty, "?limit=3" })
        {
            JsonElement page = await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId, query));
            rowCounts.Add(page.GetProperty("items").GetArrayLength());

            Assert.Equal(rowCounts.Count, probe.Samples.Count);
        }

        // The pages really did differ in size, so one event per request is not one event per row by accident.
        Assert.Equal(1, rowCounts[0]);
        Assert.True(rowCounts.Max() >= 3, "a page of at least three rows: " + string.Join(", ", rowCounts));
        Assert.All(probe.Samples, AssertIsViewed);
    }

    [Fact]
    public async Task AnEmptyPageIsAuditedToo()
    {
        (_, Guid disputeId) = await SeedWithRowsAsync(1);
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();
        // A cursor older than every row in the window: the real list, the real database, a page with nothing on it.
        string pastTheEnd = factory.Services.GetRequiredService<AgentCustomerTransactionCursorProtector>().Protect(
            new TransactionKey(DateTimeOffset.UtcNow.AddYears(-50), Guid.NewGuid()), user.Subject, disputeId);
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, AuditEvents);

        JsonElement page = await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId, "?cursor=" + Uri.EscapeDataString(pastTheEnd)));

        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        AssertIsViewed(Assert.Single(probe.Samples));
    }

    // A request that was refused read no row, so there is nothing to audit; the good request after them proves the probe is live.
    [Fact]
    public async Task ARefusedRequestIsNotAudited()
    {
        (TestCustomer customer, Guid disputeId) = await SeedWithRowsAsync(1);
        BrowserSession customerSession = await BrowserSession.SignInAsync(factory, customer.User);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, AuditEvents);

        await api.ProblemAsync(customerSession.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId)), HttpStatusCode.Forbidden);
        await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(Guid.NewGuid())), HttpStatusCode.NotFound);
        await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId, "?limit=0")), HttpStatusCode.BadRequest);
        Problem cursor = await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId, "?cursor=not-a-cursor")), HttpStatusCode.BadRequest);

        Assert.Equal("invalid_cursor", cursor.Code);
        Assert.Empty(probe.Samples);
        await api.OkAsync(agent, AgentRequests.CustomerTransactions(disputeId, "?limit=1"));
        AssertIsViewed(Assert.Single(probe.Samples));
    }

    [Fact]
    public async Task TheAuditLogCarriesTheDisputeTheAgentTheLimitAndTheCountOnly()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory, "Zanele Distinctive-Name");
        Guid disputed = await customer.AddEligibleTransactionAsync(factory);
        Guid disputeId = await customer.AddDisputeAsync(factory, disputed, DisputeStatus.Submitted);
        await customer.AddTransactionAsync(factory, TransactionStatus.Posted, TransactionDirection.Debit, DateTimeOffset.UtcNow.AddDays(-2));
        RecordingLoggerProvider logs = new();
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();

        HttpResponseMessage response = await AgentApi.SendAsync(host, agent.Request(HttpMethod.Get, AgentRequests.CustomerTransactions(disputeId, "?limit=5")));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The page did expose the customer's transactions, so their absence from the log means something.
        Assert.Contains("Test merchant", body, StringComparison.Ordinal);
        Assert.Contains("250.50", body, StringComparison.Ordinal);
        Assert.Contains("**** 0001", body, StringComparison.Ordinal);
        int returned = JsonDocument.Parse(body).RootElement.GetProperty("items").GetArrayLength();
        (string Category, EventId EventId, LogLevel Level, string Message) entry = Assert.Single(logs.Entries, candidate => candidate.EventId.Id == AgentCustomerTransactionQueries.CustomerTransactionsViewed.Id);
        Assert.Equal(5104, entry.EventId.Id);
        Assert.Equal("agent.customer_transactions.viewed", entry.EventId.Name);
        // The whole line, so any field added to it must be added here on purpose.
        Assert.Matches(ViewedLine(disputeId, user.Subject, returned), entry.Message);
        Assert.DoesNotContain(logs.Entries, candidate => new[]
        {
            "Test merchant",
            "250.50",
            "**** 0001",
            customer.User.Name,
            customer.User.Subject,
            customer.CustomerId.ToString("D"),
        }.Any(secret => candidate.Message.Contains(secret, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ViewedLine(Guid disputeId, string agentSubject, int returned) =>
        $@"^agent\.customer_transactions\.viewed dispute {disputeId} limit 5 returned {returned} by {Regex.Escape(agentSubject)} trace \S+$";

    private static void AssertIsViewed(MeasurementProbe<long>.Sample sample)
    {
        Assert.Equal(1, sample.Value);
        // The action alone: a bounded series, and no customer's data in a tag.
        KeyValuePair<string, object?> tag = Assert.Single(sample.Tags);
        Assert.Equal(("action", ViewedAction), (tag.Key, tag.Value));
    }

    /// <summary>A customer with a dispute on a transaction of theirs and <paramref name="extra"/> more recent transactions.</summary>
    private async Task<(TestCustomer Customer, Guid DisputeId)> SeedWithRowsAsync(int extra)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        Guid disputeId = await customer.AddDisputeAsync(factory, await customer.AddEligibleTransactionAsync(factory), DisputeStatus.Submitted);
        for (int index = 0; index < extra; index++)
        {
            await customer.AddTransactionAsync(factory, TransactionStatus.Posted, TransactionDirection.Debit, DateTimeOffset.UtcNow.AddDays(-1 - index));
        }

        return (customer, disputeId);
    }
}
