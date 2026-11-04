namespace DisputePortal.Domain.Eligibility;

/// <summary>
/// A dispute must be raised within <see cref="DisputePolicyOptions.WindowDays"/> calendar days (UTC) of the transaction:
/// day 120 is still eligible, day 121 is not. Calendar days, not elapsed hours, so a transaction's eligibility does not
/// flip in the middle of a day.
/// </summary>
public sealed class DisputeWindowRule(DisputePolicyOptions options) : IDisputeEligibilityRule
{
    public const string Code = "window_expired";

    public EligibilityViolation? Evaluate(DisputeEligibilityContext context)
    {
        int ageDays = DateOnly.FromDateTime(context.Now.UtcDateTime).DayNumber
            - DateOnly.FromDateTime(context.Transaction.TransactionDate.UtcDateTime).DayNumber;
        return ageDays <= options.WindowDays
            ? null
            : new EligibilityViolation(Code, $"Transactions can be disputed for {options.WindowDays} days.");
    }
}
