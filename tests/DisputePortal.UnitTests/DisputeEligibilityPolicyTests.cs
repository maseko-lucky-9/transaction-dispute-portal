using DisputePortal.Domain.Eligibility;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.UnitTests;

public sealed class DisputeEligibilityPolicyTests
{
    private static readonly DateTimeOffset Transacted = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void APostedDebitInsideTheWindowIsDisputable() =>
        Assert.Empty(Evaluate(TransactionStatus.Posted, TransactionDirection.Debit, daysLater: 10));

    [Theory]
    [InlineData(TransactionStatus.Pending)]
    [InlineData(TransactionStatus.Reversed)]
    public void OnlyAPostedTransactionIsDisputable(TransactionStatus status) =>
        Assert.Equal([PostedRule.Code], Codes(Evaluate(status, TransactionDirection.Debit, daysLater: 10)));

    [Fact]
    public void ACreditIsNotDisputable() =>
        Assert.Equal([DebitRule.Code], Codes(Evaluate(TransactionStatus.Posted, TransactionDirection.Credit, daysLater: 10)));

    // Day 120 is the last day; day 121 is out.
    [Theory]
    [InlineData(119, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void TheWindowIncludesDay120AndEndsOnDay121(int daysLater, bool disputable) =>
        Assert.Equal(disputable, Evaluate(TransactionStatus.Posted, TransactionDirection.Debit, daysLater).Count == 0);

    // Calendar days in UTC: a late-evening transaction stays eligible all through day 120, whatever the hour.
    [Fact]
    public void TheWindowCountsCalendarDaysNotElapsedHours()
    {
        DateTimeOffset lateEvening = new(2026, 3, 1, 23, 59, 0, TimeSpan.Zero);
        DateTimeOffset dayZeroMidnight = new(lateEvening.Date, TimeSpan.Zero);
        FixedClock lastMinuteOfDay120 = new(dayZeroMidnight.AddDays(120).AddHours(23).AddMinutes(59));
        FixedClock firstMinuteOfDay121 = new(dayZeroMidnight.AddDays(121));

        Assert.Empty(Policy(lastMinuteOfDay120).Evaluate(new TransactionSnapshot(TransactionStatus.Posted, TransactionDirection.Debit, lateEvening)));
        Assert.Equal([DisputeWindowRule.Code], Codes(Policy(firstMinuteOfDay121).Evaluate(new TransactionSnapshot(TransactionStatus.Posted, TransactionDirection.Debit, lateEvening))));
    }

    [Fact]
    public void EveryViolationIsReportedTogetherInRuleOrder() =>
        Assert.Equal(
            [PostedRule.Code, DebitRule.Code, DisputeWindowRule.Code],
            Codes(Evaluate(TransactionStatus.Pending, TransactionDirection.Credit, daysLater: 200)));

    [Fact]
    public void TheWindowLengthComesFromTheOptions()
    {
        DisputeEligibilityPolicy thirtyDays = new([new DisputeWindowRule(new DisputePolicyOptions { WindowDays = 30 })], new FixedClock(Transacted.AddDays(31)));

        Assert.Equal([DisputeWindowRule.Code], Codes(thirtyDays.Evaluate(new TransactionSnapshot(TransactionStatus.Posted, TransactionDirection.Debit, Transacted))));
    }

    [Fact]
    public void EachViolationCarriesAMessage() =>
        Assert.All(Evaluate(TransactionStatus.Pending, TransactionDirection.Credit, daysLater: 200), violation => Assert.False(string.IsNullOrWhiteSpace(violation.Message)));

    private static IReadOnlyList<EligibilityViolation> Evaluate(TransactionStatus status, TransactionDirection direction, int daysLater) =>
        Policy(new FixedClock(Transacted.AddDays(daysLater))).Evaluate(new TransactionSnapshot(status, direction, Transacted));

    private static DisputeEligibilityPolicy Policy(TimeProvider clock) =>
        new([new PostedRule(), new DebitRule(), new DisputeWindowRule(new DisputePolicyOptions())], clock);

    private static IEnumerable<string> Codes(IEnumerable<EligibilityViolation> violations) => violations.Select(violation => violation.Code);
}
