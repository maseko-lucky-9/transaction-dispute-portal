namespace DisputePortal.Domain.Eligibility;

/// <summary>One scheme rule. Adding a rule is adding a class (open/closed); the policy composes them.</summary>
public interface IDisputeEligibilityRule
{
    /// <returns>The violation, or null when the rule is satisfied or does not apply.</returns>
    EligibilityViolation? Evaluate(DisputeEligibilityContext context);
}
