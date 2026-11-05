namespace DisputePortal.Domain.Eligibility;

/// <summary>Tunable scheme limits (configuration section <c>DisputePolicy</c>).</summary>
public sealed class DisputePolicyOptions
{
    public const string Section = "DisputePolicy";

    /// <summary>A transaction may be disputed up to and including this many calendar days (UTC) after it happened.</summary>
    public int WindowDays { get; init; } = 120;

    /// <summary>A customer may have at most this many disputes in Submitted or UnderReview at once.</summary>
    public int MaxActiveDisputesPerCustomer { get; init; } = 20;

    /// <summary>A transaction may be disputed at most this many times, withdrawn disputes included.</summary>
    public int MaxDisputesPerTransaction { get; init; } = 3;
}
