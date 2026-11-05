using DisputePortal.Domain.Transactions;

namespace DisputePortal.Domain.Eligibility;

/// <summary>Only a settled transaction can be disputed: a pending one may still change, a reversed one has been returned.</summary>
public sealed class PostedRule : IDisputeEligibilityRule
{
    public const string Code = "not_posted";

    public EligibilityViolation? Evaluate(DisputeEligibilityContext context) =>
        context.Transaction.Status == TransactionStatus.Posted
            ? null
            : new EligibilityViolation(Code, "Only posted transactions can be disputed.");
}
