using DisputePortal.Domain.Common;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Domain.Eligibility;

/// <summary>
/// Everything a rule may look at, taken once so every rule judges the same instant. The optional inputs are known only
/// when a dispute is being submitted (or counted for the list); a rule whose input is absent does not apply.
/// </summary>
public sealed record DisputeEligibilityContext(TransactionSnapshot Transaction, DateTimeOffset Now)
{
    /// <summary>The amount the customer disputes (the whole transaction when they do not say).</summary>
    public Money? DisputedAmount { get; init; }

    /// <summary>The customer's disputes in Submitted or UnderReview.</summary>
    public int? ActiveDisputeCount { get; init; }

    /// <summary>Every dispute ever raised on this transaction, withdrawn ones included.</summary>
    public int? TransactionDisputeCount { get; init; }
}
