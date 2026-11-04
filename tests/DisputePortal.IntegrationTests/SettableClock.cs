namespace DisputePortal.IntegrationTests;

/// <summary>A clock a test moves by hand, for a host that must judge "now" at a chosen instant.</summary>
public sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
