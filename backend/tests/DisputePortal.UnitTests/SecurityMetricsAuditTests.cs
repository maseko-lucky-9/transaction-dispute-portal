namespace DisputePortal.UnitTests;

/// <summary><c>audit.events{action}</c> has a closed set of action values: the tag stays bounded, and dashboards key on the literals.</summary>
public sealed class SecurityMetricsAuditTests : IDisposable
{
    private readonly MeterSandbox meters = new();

    public void Dispose() => meters.Dispose();

    // Literals on purpose: an edit to a constant would silently break the runbook's alert and dashboard queries.
    [Theory]
    [InlineData("agent_queue_viewed")]
    [InlineData("agent_dispute_viewed")]
    [InlineData("agent_dispute_transitioned")]
    public void AKnownActionIsCountedOnceWithTheActionAsTheOnlyTag(string action)
    {
        meters.Security.Audit(action);

        MeterSandbox.Measured sample = Assert.Single(meters.Of("audit.events"));
        Assert.Equal(1, sample.Value);
        KeyValuePair<string, object?> tag = Assert.Single(sample.Tags);
        Assert.Equal(("action", action), (tag.Key, tag.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("agent_queue_viewed ")]
    [InlineData("AGENT_QUEUE_VIEWED")]
    [InlineData("agent_queue_viewed:8d4b2f3e-0000-0000-0000-000000000000")]
    [InlineData("agent_queue")]
    public void AnythingElseIsRefusedSoNoUnboundedValueBecomesATag(string action)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => meters.Security.Audit(action));

        Assert.Empty(meters.Of("audit.events"));
    }
}
