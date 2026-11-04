namespace DisputePortal.Domain.Eligibility;

/// <summary>Tunable scheme limits (configuration section <c>DisputePolicy</c>).</summary>
public sealed class DisputePolicyOptions
{
    public const string Section = "DisputePolicy";

    /// <summary>A transaction may be disputed up to and including this many calendar days (UTC) after it happened.</summary>
    public int WindowDays { get; init; } = 120;
}
