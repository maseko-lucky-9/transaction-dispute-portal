using System.Net;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// <c>security.events{type=bola_404}</c>: one count each time a customer is told 404 for a transaction or dispute the
/// owner filter hides. The counter cannot tell a foreign id from a missing one (see <see cref="SecurityMetrics.Bola404"/>).
/// </summary>
[Collection("api")]
public sealed class Bola404MetricTests(ApiFactory api)
{
    private const string SecurityEvents = "security.events";

    [Fact]
    public async Task ReadingAnotherCustomersDisputeCountsOneBola404AndTheOwnersReadCountsNone()
    {
        (Guid theirs, BrowserSession owner) = await SeedDisputeWithOwnerAsync();
        BrowserSession intruder = await BrowserSession.SignInAsync(api, (await TestCustomer.CreateAsync(api)).User);
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(api, SecurityMetrics.MeterName, SecurityEvents);

        HttpResponseMessage ownRead = await AgentApi.SendAsync(api, owner.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{theirs}"));
        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);
        Assert.Empty(probe.Samples);
        HttpResponseMessage foreignRead = await AgentApi.SendAsync(api, intruder.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{theirs}"));

        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        MeasurementProbe<long>.Sample sample = Assert.Single(probe.Samples);
        Assert.Equal(1, sample.Value);
        // Only the type: an id in a tag would be an unbounded series and a customer's data in the metrics.
        KeyValuePair<string, object?> tag = Assert.Single(sample.Tags);
        Assert.Equal(("type", "bola_404"), (tag.Key, tag.Value));
        Assert.DoesNotContain(theirs.ToString(), string.Join(' ', sample.Tags.Values), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("foreign-dispute-read")]
    [InlineData("foreign-dispute-withdraw")]
    [InlineData("foreign-transaction-read")]
    [InlineData("foreign-transaction-submit")]
    [InlineData("missing-dispute-read")]
    [InlineData("missing-transaction-read")]
    public async Task EveryNotFoundAnsweredToACustomerCountsExactlyOne(string path)
    {
        TestCustomer victim = await TestCustomer.CreateAsync(api);
        Guid theirTransaction = await victim.AddEligibleTransactionAsync(api);
        Guid theirDispute = await victim.AddDisputeAsync(api, await victim.AddEligibleTransactionAsync(api), DisputeStatus.Submitted);
        BrowserSession intruder = await BrowserSession.SignInAsync(api, (await TestCustomer.CreateAsync(api)).User);
        HttpRequestMessage request = path switch
        {
            "foreign-dispute-read" => intruder.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{theirDispute}"),
            "foreign-dispute-withdraw" => DisputeRequests.Withdraw(intruder, theirDispute, "\"1\""),
            "foreign-transaction-read" => intruder.Request(HttpMethod.Get, $"/api/v1/transactions/{theirTransaction}"),
            "foreign-transaction-submit" => DisputeRequests.Submit(intruder, Guid.NewGuid(), theirTransaction),
            "missing-dispute-read" => intruder.Request(HttpMethod.Get, $"{DisputeRequests.Disputes}/{Guid.NewGuid()}"),
            "missing-transaction-read" => intruder.Request(HttpMethod.Get, $"/api/v1/transactions/{Guid.NewGuid()}"),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        };
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(api, SecurityMetrics.MeterName, SecurityEvents);

        HttpResponseMessage response = await AgentApi.SendAsync(api, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        MeasurementProbe<long>.Sample sample = Assert.Single(probe.Samples);
        Assert.Equal(1, sample.Value);
        Assert.Equal("bola_404", sample.Tags["type"]);
    }

    // Agents may read every dispute, so their 404 is a missing id and not a probe of someone else's data.
    [Fact]
    public async Task AnAgentsNotFoundIsNotCounted()
    {
        (_, BrowserSession agent) = await new AgentApi(api).NewAgentAsync();
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(api, SecurityMetrics.MeterName, SecurityEvents);

        HttpResponseMessage response = await AgentApi.SendAsync(api, agent.Request(HttpMethod.Get, AgentRequests.Detail(Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(probe.Samples);
    }

    private async Task<(Guid DisputeId, BrowserSession Owner)> SeedDisputeWithOwnerAsync()
    {
        TestCustomer owner = await TestCustomer.CreateAsync(api);
        Guid id = await owner.AddDisputeAsync(api, await owner.AddEligibleTransactionAsync(api), DisputeStatus.Submitted);
        return (id, await BrowserSession.SignInAsync(api, owner.User));
    }
}
