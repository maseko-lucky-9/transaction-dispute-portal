using DisputePortal.Domain.Transactions;

namespace DisputePortal.Domain.Eligibility;

/// <summary>
/// Runs every rule and returns every violation, never just the first, so the customer can see all reasons at once
/// (and a submission gets one 422 listing them all). Rules run in the order given.
/// </summary>
public sealed class DisputeEligibilityPolicy(IEnumerable<IDisputeEligibilityRule> rules, TimeProvider time)
{
    private readonly IReadOnlyList<IDisputeEligibilityRule> rules = [.. rules];

    public IReadOnlyList<EligibilityViolation> Evaluate(TransactionSnapshot transaction)
    {
        DisputeEligibilityContext context = new(transaction, time.GetUtcNow());
        return [.. rules.Select(rule => rule.Evaluate(context)).OfType<EligibilityViolation>()];
    }
}
