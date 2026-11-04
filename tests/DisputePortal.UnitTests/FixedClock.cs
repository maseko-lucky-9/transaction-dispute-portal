namespace DisputePortal.UnitTests;

/// <summary>A clock that reads whatever the test sets.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
