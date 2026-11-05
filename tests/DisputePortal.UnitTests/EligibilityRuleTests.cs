using DisputePortal.Domain.Common;
using DisputePortal.Domain.Eligibility;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.UnitTests;

/// <summary>The amount and cap rules, and the whole chain in registration order.</summary>
public sealed class EligibilityRuleTests
{
    private static readonly DateTimeOffset Transacted = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TransactionSnapshot PostedDebit = new(TransactionStatus.Posted, TransactionDirection.Debit, Transacted, Money.Of(250.50m, "ZAR"));
    private static readonly DisputePolicyOptions Options = new();

    [Theory]
    [InlineData("250.50", null)]
    [InlineData("0.01", null)]
    [InlineData("125.25", null)]
    [InlineData("250.51", AmountRule.ExceedsTransactionCode)]
    [InlineData("0", AmountRule.NotPositiveCode)]
    public void TheAmountIsMoreThanZeroAndAtMostTheTransaction(string amount, string? code) =>
        Assert.Equal(code, new AmountRule().Evaluate(Context() with { DisputedAmount = Money.Of(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "ZAR") })?.Code);

    [Fact]
    public void RulesWithoutTheirInputDoNotApply()
    {
        Assert.Null(new AmountRule().Evaluate(Context()));
        Assert.Null(new ActiveDisputeCapRule(Options).Evaluate(Context()));
        Assert.Null(new TransactionDisputeCapRule(Options).Evaluate(Context()));
    }

    // The 21st open dispute is refused: 20 already open is the ceiling.
    [Theory]
    [InlineData(19, null)]
    [InlineData(20, ActiveDisputeCapRule.Code)]
    public void ACustomerHasAtMost20OpenDisputes(int open, string? code) =>
        Assert.Equal(code, new ActiveDisputeCapRule(Options).Evaluate(Context() with { ActiveDisputeCount = open })?.Code);

    // The 4th dispute on one transaction is refused, withdrawn ones included.
    [Theory]
    [InlineData(2, null)]
    [InlineData(3, TransactionDisputeCapRule.Code)]
    public void ATransactionIsDisputedAtMost3Times(int earlier, string? code) =>
        Assert.Equal(code, new TransactionDisputeCapRule(Options).Evaluate(Context() with { TransactionDisputeCount = earlier })?.Code);

    [Fact]
    public void TheCapsComeFromTheOptions()
    {
        DisputePolicyOptions tight = new() { MaxActiveDisputesPerCustomer = 1, MaxDisputesPerTransaction = 1 };

        Assert.NotNull(new ActiveDisputeCapRule(tight).Evaluate(Context() with { ActiveDisputeCount = 1 }));
        Assert.NotNull(new TransactionDisputeCapRule(tight).Evaluate(Context() with { TransactionDisputeCount = 1 }));
    }

    [Fact]
    public void TheWholeChainReportsEveryViolationInRegistrationOrder()
    {
        DisputeEligibilityPolicy policy = new(
            [
                new PostedRule(), new DebitRule(), new DisputeWindowRule(Options), new AmountRule(),
                new ActiveDisputeCapRule(Options), new TransactionDisputeCapRule(Options),
            ],
            new FixedClock(Transacted.AddDays(200)));
        TransactionSnapshot pendingCredit = PostedDebit with { Status = TransactionStatus.Pending, Direction = TransactionDirection.Credit };

        IReadOnlyList<EligibilityViolation> violations = policy.Evaluate(policy.ContextFor(pendingCredit) with
        {
            DisputedAmount = Money.Of(999m, "ZAR"),
            ActiveDisputeCount = 20,
            TransactionDisputeCount = 3,
        });

        Assert.Equal(
            [
                PostedRule.Code, DebitRule.Code, DisputeWindowRule.Code, AmountRule.ExceedsTransactionCode,
                ActiveDisputeCapRule.Code, TransactionDisputeCapRule.Code,
            ],
            violations.Select(violation => violation.Code));
        Assert.All(violations, violation => Assert.False(string.IsNullOrWhiteSpace(violation.Message)));
    }

    private static DisputeEligibilityContext Context() => new(PostedDebit, Transacted.AddDays(1));
}
