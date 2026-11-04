using System.Net;
using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// <c>disputes.resolution.duration</c> (seconds, tag <c>to</c>): recorded once when an agent accepts or rejects a dispute,
/// measured from the dispute's creation to the host's clock, which these tests set.
/// </summary>
[Collection("api")]
public sealed class ResolutionDurationMetricTests(ApiFactory api)
{
    private const string ResolutionDuration = "disputes.resolution.duration";

    private static readonly TimeSpan Elapsed = TimeSpan.FromMinutes(90);

    private readonly AgentApi agents = new(api);

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    public async Task ADecisionRecordsOneMeasurementOfTheSecondsSinceSubmissionTaggedWithTheOutcome(string outcome)
    {
        DateTimeOffset createdAt = WholeSecondsAgo(TimeSpan.FromDays(1));
        (_, Guid id) = await agents.SeedDisputeAsync(DisputeStatus.UnderReview, createdAt);
        await MoveUpdatedAtAsync(id, createdAt + TimeSpan.FromMinutes(30));
        (_, BrowserSession agent) = await agents.NewAgentAsync();
        string etag = await agents.ETagAsync(agent, id);
        await using WebApplicationFactory<Program> clocked = ClockedHost(createdAt + Elapsed);
        using MeasurementProbe<double> probe = MeasurementProbe<double>.On(clocked, DisputeMetrics.MeterName, ResolutionDuration);

        HttpResponseMessage response = await AgentApi.SendAsync(clocked, AgentRequests.Transition(agent, id, etag, outcome, "Checked with the merchant."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        MeasurementProbe<double>.Sample sample = Assert.Single(probe.Samples);
        Assert.Equal(Elapsed.TotalSeconds, sample.Value);
        Assert.Equal("s", probe.Unit);
        KeyValuePair<string, object?> tag = Assert.Single(sample.Tags);
        Assert.Equal(("to", outcome), (tag.Key, tag.Value));
    }

    // Only a decision is a resolution; a refused or lost move changed nothing, so it must not add a measurement.
    [Fact]
    public async Task AReviewAStaleTagAndAnIllegalMoveRecordNoDuration()
    {
        DateTimeOffset createdAt = WholeSecondsAgo(TimeSpan.FromDays(1));
        (_, Guid id) = await agents.SeedDisputeAsync(DisputeStatus.Submitted, createdAt);
        (_, BrowserSession agent) = await agents.NewAgentAsync();
        string etag = await agents.ETagAsync(agent, id);
        await using WebApplicationFactory<Program> clocked = ClockedHost(createdAt + Elapsed);
        using MeasurementProbe<double> probe = MeasurementProbe<double>.On(clocked, DisputeMetrics.MeterName, ResolutionDuration);

        HttpResponseMessage stale = await AgentApi.SendAsync(clocked, AgentRequests.Transition(agent, id, "\"1\"", "Accepted", "Checked."));
        HttpResponseMessage illegal = await AgentApi.SendAsync(clocked, AgentRequests.Transition(agent, id, etag, "Accepted", "Checked."));
        HttpResponseMessage review = await AgentApi.SendAsync(clocked, AgentRequests.Transition(agent, id, etag, "UnderReview"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, illegal.StatusCode);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Empty(probe.Samples);
    }

    // Both agents load the dispute and pass If-Match; the second UPDATE ... WHERE xmin = N finds nothing, so one decision lands.
    [Fact]
    public async Task TwoAgentsDecidingAtOnceRecordOneMeasurementForTheDecisionThatWasStored()
    {
        DateTimeOffset createdAt = WholeSecondsAgo(TimeSpan.FromDays(1));
        (_, Guid id) = await agents.SeedDisputeAsync(DisputeStatus.UnderReview, createdAt);
        (_, BrowserSession first) = await agents.NewAgentAsync();
        (_, BrowserSession second) = await agents.NewAgentAsync();
        string etag = await agents.ETagAsync(first, id);
        await using WebApplicationFactory<Program> gated = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            Rendezvous afterFind = new(2);
            services.AddSingleton<TimeProvider>(new SettableClock(createdAt + Elapsed));
            services.AddScoped<AgentDisputeRepository>();
            services.AddScoped<IAgentDisputeRepository>(provider =>
                new GatedAgentDisputeRepository(provider.GetRequiredService<AgentDisputeRepository>(), afterFind));
        }));
        using MeasurementProbe<double> probe = MeasurementProbe<double>.On(gated, DisputeMetrics.MeterName, ResolutionDuration);

        HttpResponseMessage[] responses = await Task.WhenAll(
            AgentApi.SendAsync(gated, AgentRequests.Transition(first, id, etag, "Accepted", "First decision.")),
            AgentApi.SendAsync(gated, AgentRequests.Transition(second, id, etag, "Rejected", "Second decision.")));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        MeasurementProbe<double>.Sample sample = Assert.Single(probe.Samples);
        Assert.Equal(Elapsed.TotalSeconds, sample.Value);
    }

    // A dispute that was reviewed first has been touched since it was created: the duration still runs from creation.
    private async Task MoveUpdatedAtAsync(Guid id, DateTimeOffset updatedAt)
    {
        await using NpgsqlConnection connection = new(api.AdminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("UPDATE disputes SET updated_at = @updated WHERE id = @id", connection);
        command.Parameters.AddWithValue("updated", updatedAt);
        command.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    // Postgres keeps microseconds: a whole second makes the expected value exact.
    private static DateTimeOffset WholeSecondsAgo(TimeSpan age) =>
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.Add(-age).ToUnixTimeSeconds());

    private WebApplicationFactory<Program> ClockedHost(DateTimeOffset now) =>
        api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new SettableClock(now))));
}
