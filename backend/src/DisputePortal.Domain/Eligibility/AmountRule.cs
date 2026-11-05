namespace DisputePortal.Domain.Eligibility;

/// <summary>The disputed amount is more than zero and at most the transaction's amount (a partial dispute is allowed).</summary>
public sealed class AmountRule : IDisputeEligibilityRule
{
    public const string NotPositiveCode = "amount_not_positive";
    public const string ExceedsTransactionCode = "amount_exceeds_transaction";

    public EligibilityViolation? Evaluate(DisputeEligibilityContext context) => context.DisputedAmount switch
    {
        null => null,
        { Amount: <= 0 } => new EligibilityViolation(NotPositiveCode, "The disputed amount must be more than zero."),
        { } amount when amount.IsGreaterThan(context.Transaction.Amount) =>
            new EligibilityViolation(ExceedsTransactionCode, "The disputed amount cannot be more than the transaction amount."),
        _ => null,
    };
}
