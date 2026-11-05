namespace DisputePortal.Domain.Eligibility;

/// <summary>
/// At most <see cref="DisputePolicyOptions.MaxActiveDisputesPerCustomer"/> open disputes per customer. A policy ceiling,
/// counted before the insert: two submits racing at the ceiling can overshoot it by one (ADR-003).
/// </summary>
public sealed class ActiveDisputeCapRule(DisputePolicyOptions options) : IDisputeEligibilityRule
{
    public const string Code = "too_many_active_disputes";

    public EligibilityViolation? Evaluate(DisputeEligibilityContext context) =>
        context.ActiveDisputeCount is { } count && count >= options.MaxActiveDisputesPerCustomer
            ? new EligibilityViolation(Code, $"You can have at most {options.MaxActiveDisputesPerCustomer} open disputes at a time.")
            : null;
}
