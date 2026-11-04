using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The queue shows every customer's display name and masked account number, so loading a page is audited like opening a
/// dispute: <c>audit.events{action=agent_queue_viewed}</c> and one <c>agent.queue.viewed</c> log line, once per page.
/// </summary>
[Collection("api")]
public sealed class AgentQueueAuditTests(ApiFactory factory)
{
    private const string AuditEvents = "audit.events";
    private const string QueueViewedAction = "agent_queue_viewed";

    // Older than the disputes of every other test (they start in 2001): the queue is oldest first, so a first page holds
    // these rows whatever else the shared database has.
    private static readonly DateTimeOffset Oldest = new(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly AgentApi api = new(factory);

    [Fact]
    public async Task EveryQueueRequestIsOneAuditEventWhateverItsRowCount()
    {
        await SeedOldestAsync(3);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, AuditEvents);
        List<int> rowCounts = [];

        foreach (string query in new[] { "?limit=1", "?limit=3", "?limit=100", string.Empty, "?status=Submitted&limit=2" })
        {
            JsonElement page = await api.OkAsync(agent, AgentRequests.Queue + query);
            rowCounts.Add(page.GetProperty("items").GetArrayLength());

            Assert.Equal(rowCounts.Count, probe.Samples.Count);
        }

        // The pages really did differ in size, so one event per request is not one event per row by accident.
        Assert.Equal(1, rowCounts[0]);
        Assert.True(rowCounts.Max() >= 3, "a page of at least three rows: " + string.Join(", ", rowCounts));
        Assert.All(probe.Samples, AssertIsQueueViewed);
    }

    [Fact]
    public async Task AnEmptyPageIsAuditedToo()
    {
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();
        // A cursor past the newest row: the real queue, the real database, a page with nothing on it.
        string pastTheEnd = factory.Services.GetRequiredService<AgentCursorProtector>().Protect(
            new DisputeKey(DateTimeOffset.UtcNow.AddYears(50), Guid.NewGuid()), user.Subject, AgentQueueFilter.Parse(new AgentQueueRequest(null, null, null)));
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, AuditEvents);

        JsonElement page = await api.OkAsync(agent, AgentRequests.Queue + "?cursor=" + Uri.EscapeDataString(pastTheEnd));

        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        AssertIsQueueViewed(Assert.Single(probe.Samples));
    }

    // A request that was refused read no row, so there is nothing to audit; the good request after them proves the probe is live.
    [Fact]
    public async Task ARefusedQueueRequestIsNotAudited()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        BrowserSession customerSession = await BrowserSession.SignInAsync(factory, customer.User);
        (_, BrowserSession agent) = await api.NewAgentAsync();
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(factory, SecurityMetrics.MeterName, AuditEvents);

        await api.ProblemAsync(customerSession.Request(HttpMethod.Get, AgentRequests.Queue), HttpStatusCode.Forbidden);
        await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.Queue + "?status=Done"), HttpStatusCode.BadRequest);
        Problem cursor = await api.ProblemAsync(agent.Request(HttpMethod.Get, AgentRequests.Queue + "?cursor=not-a-cursor"), HttpStatusCode.BadRequest);

        Assert.Equal("invalid_cursor", cursor.Code);
        Assert.Empty(probe.Samples);
        await api.OkAsync(agent, AgentRequests.Queue + "?limit=1");
        AssertIsQueueViewed(Assert.Single(probe.Samples));
    }

    [Fact]
    public async Task TheQueueAuditLogCarriesTheAgentTheFilterAndCountsOnly()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory, "Zanele Distinctive-Name");
        // A day before the rest of the oldest rows: first on the page, so a leak of the first row's details cannot hide.
        await customer.AddDisputeAsync(factory, await customer.AddEligibleTransactionAsync(factory), DisputeStatus.Submitted, Oldest.AddDays(-1));
        RecordingLoggerProvider logs = new();
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
        (IdpUser user, BrowserSession agent) = await api.NewAgentAsync();

        HttpResponseMessage response = await AgentApi.SendAsync(host, agent.Request(HttpMethod.Get, AgentRequests.Queue + "?limit=5"));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The page did expose the customer's details, so their absence from the log means something.
        Assert.Contains(customer.User.Name, body, StringComparison.Ordinal);
        Assert.Contains("**** 0001", body, StringComparison.Ordinal);
        int returned = JsonDocument.Parse(body).RootElement.GetProperty("items").GetArrayLength();
        string line = Assert.Single(logs.Entries, entry => entry.EventId.Id == AgentQueries.QueueViewed.Id).Message;
        // The whole line, so any field added to it must be added here on purpose.
        Assert.Matches(QueueViewedLine(user.Subject, returned), line);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(customer.User.Name, StringComparison.Ordinal)
            || entry.Message.Contains("**** 0001", StringComparison.Ordinal)
            || entry.Message.Contains(customer.User.Subject, StringComparison.Ordinal)
            || entry.Message.Contains("Inserted by a test.", StringComparison.Ordinal));
    }

    private static string QueueViewedLine(string agentSubject, int returned) =>
        $@"^agent\.queue\.viewed filter open limit 5 returned {returned} by {Regex.Escape(agentSubject)} trace \S+$";

    private static void AssertIsQueueViewed(MeasurementProbe<long>.Sample sample)
    {
        Assert.Equal(1, sample.Value);
        // The action alone: a bounded series, and no customer's data in a tag.
        KeyValuePair<string, object?> tag = Assert.Single(sample.Tags);
        Assert.Equal(("action", QueueViewedAction), (tag.Key, tag.Value));
    }

    private async Task SeedOldestAsync(int count)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(factory);
        for (int index = 0; index < count; index++)
        {
            await customer.AddDisputeAsync(factory, await customer.AddEligibleTransactionAsync(factory), DisputeStatus.Submitted, Oldest.AddMinutes(index));
        }
    }
}
