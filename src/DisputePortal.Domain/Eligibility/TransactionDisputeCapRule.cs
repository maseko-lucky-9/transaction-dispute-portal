namespace DisputePortal.Domain.Eligibility;

/// <summary>At most <see cref="DisputePolicyOptions.MaxDisputesPerTransaction"/> disputes ever per transaction, withdrawn ones included.</summary>
public sealed class TransactionDisputeCapRule(DisputePolicyOptions options) : IDisputeEligibilityRule
{
    public const string Code = "too_many_disputes_for_transaction";

    public EligibilityViolation? Evaluate(DisputeEligibilityContext context) =>
        context.TransactionDisputeCount is { } count && count >= options.MaxDisputesPerTransaction
            ? new EligibilityViolation(Code, $"A transaction can be disputed at most {options.MaxDisputesPerTransaction} times.")
            : null;
}
