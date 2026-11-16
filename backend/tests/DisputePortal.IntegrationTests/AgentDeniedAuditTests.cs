using System.Net;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// ADR-019: an accept, reject or release refused because another agent is reviewing (409 not_assignee) is one
/// <c>audit.events{action=agent_dispute_denied}</c> and one <c>agent.dispute.denied</c> line with the dispute and the
/// refused agent only: never the reviewing agent, the customer or the note.
/// </summary>
[Collection("api")]
public sealed class AgentDeniedAuditTests(ApiFactory factory)
{
    private const string AuditEvents = "audit.events";
    private const string Note = "Checked with the merchant, refund agreed.";

    private readonly AgentApi api = new(factory);

    [Theory]
    [InlineData("Accepted", Note)]
    [InlineData("Submitted", null)]
    public async Task ARefusedMoveIsLoggedAndCountedWithTheDisputeAndTheRefusedAgentOnly(string to, string? note)
    {
        (IdpUser reviewer, _) = await api.NewAgentAsync();
        (TestCustomer customer, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview, assignee: reviewer.Subject);
        (IdpUser intruder, BrowserSession session) = await api.NewAgentAsync();
        string etag = await api.ETagAsync(session, id);
        RecordingLoggerProvider logs = new();
        await using WebApplicationFactory<Program> host = WithLogs(logs);
        using MeasurementProbe<long> audits = MeasurementProbe<long>.On(host, SecurityMetrics.MeterName, AuditEvents);

        HttpResponseMessage response = await AgentApi.SendAsync(host, AgentRequests.Transition(session, id, etag, to, note));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        (string Category, EventId EventId, LogLevel Level, string Message) entry = Assert.Single(logs.Entries, logged => logged.EventId.Id == AgentCommands.DisputeDenied.Id);
        Assert.Equal((5105, "agent.dispute.denied"), (entry.EventId.Id, entry.EventId.Name));
        // The whole line, so any field added to it must be added here on purpose.
        Assert.Equal($"agent.dispute.denied {id} by {intruder.Subject}", entry.Message);
        Assert.DoesNotContain(logs.Entries, logged => logged.Message.Contains(reviewer.Subject, StringComparison.Ordinal)
            || logged.Message.Contains(customer.User.Subject, StringComparison.Ordinal)
            || logged.Message.Contains(Note, StringComparison.Ordinal));
        MeasurementProbe<long>.Sample audit = Assert.Single(audits.Samples);
        Assert.Equal(1, audit.Value);
        KeyValuePair<string, object?> tag = Assert.Single(audit.Tags);
        Assert.Equal(("action", "agent_dispute_denied"), (tag.Key, tag.Value));
    }

    [Fact]
    public async Task TheAssigneesOwnMoveIsNotLoggedOrCountedAsDenied()
    {
        (IdpUser reviewer, BrowserSession agent) = await api.NewAgentAsync();
        (_, Guid id) = await api.SeedDisputeAsync(DisputeStatus.UnderReview, assignee: reviewer.Subject);
        string etag = await api.ETagAsync(agent, id);
        RecordingLoggerProvider logs = new();
        await using WebApplicationFactory<Program> host = WithLogs(logs);
        using MeasurementProbe<long> audits = MeasurementProbe<long>.On(host, SecurityMetrics.MeterName, AuditEvents);

        HttpResponseMessage response = await AgentApi.SendAsync(host, AgentRequests.Transition(agent, id, etag, "Accepted", Note));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(logs.Entries, logged => logged.EventId.Id == AgentCommands.DisputeDenied.Id);
        Assert.Contains(logs.Entries, logged => logged.EventId.Id == AgentCommands.DisputeTransitioned.Id);
        Assert.Equal(new object?[] { SecurityMetrics.AgentDisputeTransitioned }, audits.Samples.Select(sample => sample.Tags["action"]));
    }

    private WebApplicationFactory<Program> WithLogs(RecordingLoggerProvider logs) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
}
