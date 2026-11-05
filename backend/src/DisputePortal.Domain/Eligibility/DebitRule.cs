using DisputePortal.Domain.Transactions;

namespace DisputePortal.Domain.Eligibility;

/// <summary>Only money that left the account can be disputed.</summary>
public sealed class DebitRule : IDisputeEligibilityRule
{
    public const string Code = "not_debit";

    public EligibilityViolation? Evaluate(DisputeEligibilityContext context) =>
        context.Transaction.Direction == TransactionDirection.Debit
            ? null
            : new EligibilityViolation(Code, "Only debits can be disputed.");
}
