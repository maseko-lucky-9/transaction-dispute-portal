using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DisputePortal.UnitTests;

/// <summary>
/// The list's two rules, called without the database: which UTC instant the window starts at (the clock and the disputed
/// transaction decide it), and which disputes may be listed at all (only those still under review).
/// </summary>
public sealed class AgentCustomerTransactionQueriesTests : IDisposable
{
    private const string Agent = "agent-1";
    private static readonly Guid Dispute = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    private readonly MeterSandbox meters = new();
    private readonly IAgentCustomerTransactionRepository transactions = Substitute.For<IAgentCustomerTransactionRepository>();
    private readonly FixedClock clock = new(DateTimeOffset.Parse("2026-11-16T12:00:00Z"));
    private readonly AgentCustomerTransactionQueries queries;

    public AgentCustomerTransactionQueriesTests()
    {
        transactions.ListAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<AgentCustomerTransaction>>([]));
        queries = new AgentCustomerTransactionQueries(
            transactions,
            new AgentCustomerTransactionCursorProtector(new EphemeralDataProtectionProvider()),
            clock,
            meters.Security,
            NullLogger<AgentCustomerTransactionQueries>.Instance);
    }

    public void Dispose() => meters.Dispose();

    // The bound is midnight UTC of a whole day, inclusive: the second before the day turns still reads the old day.
    [Theory]
    [InlineData("2026-11-16T23:59:59Z", "2026-11-10T10:00:00Z", "2026-08-18")]
    [InlineData("2026-11-17T00:00:00Z", "2026-11-10T10:00:00Z", "2026-08-19")]
    [InlineData("2026-11-17T00:00:01Z", "2026-11-10T10:00:00Z", "2026-08-19")]
    // The disputed transaction is the earlier day: the window reaches back to its day, from midnight, whatever its time.
    [InlineData("2026-11-16T12:00:00Z", "2026-06-30T23:59:59Z", "2026-06-30")]
    [InlineData("2026-11-16T12:00:00Z", "2026-08-17T23:59:59Z", "2026-08-17")]
    // On the same day as the 90-day mark, or after it: the 90-day mark stands.
    [InlineData("2026-11-16T12:00:00Z", "2026-08-18T15:00:00Z", "2026-08-18")]
    [InlineData("2026-11-16T12:00:00Z", "2026-08-19T00:00:00Z", "2026-08-18")]
    public async Task TheWindowStartsAtMidnightUtcOfTheEarlierOfTheNinetyDayMarkAndTheDisputedDay(string now, string disputedAt, string expectedDay)
    {
        clock.Now = DateTimeOffset.Parse(now);
        GivenDispute(DisputeStatus.UnderReview, DateTimeOffset.Parse(disputedAt));

        AgentCustomerTransactionsResponse page = await queries.ListAsync(Dispute, new AgentCustomerTransactionsRequest(null, null), Agent, "trace", CancellationToken.None);

        Assert.Equal(DateOnly.Parse(expectedDay), page.From);
        await transactions.Received(1).ListAsync(
            Customer, DateTimeOffset.Parse(expectedDay + "T00:00:00Z"), null, 26, CancellationToken.None);
    }

    [Theory]
    [InlineData(DisputeStatus.Submitted)]
    [InlineData(DisputeStatus.UnderReview)]
    public async Task ADisputeUnderReviewIsListedAndAuditedOnce(DisputeStatus status)
    {
        GivenDispute(status, clock.Now.AddDays(-5));

        await queries.ListAsync(Dispute, new AgentCustomerTransactionsRequest(null, null), Agent, "trace", CancellationToken.None);

        Assert.Single(meters.Of("audit.events"));
    }

    [Theory]
    [InlineData(DisputeStatus.Accepted)]
    [InlineData(DisputeStatus.Rejected)]
    [InlineData(DisputeStatus.Withdrawn)]
    public async Task ADisputeNoLongerUnderReviewIsAMissingIdThatReadsAndAuditsNothing(DisputeStatus status)
    {
        GivenDispute(status, clock.Now.AddDays(-5));

        NotFoundException refusal = await Assert.ThrowsAsync<NotFoundException>(
            () => queries.ListAsync(Dispute, new AgentCustomerTransactionsRequest(null, null), Agent, "trace", CancellationToken.None));

        Assert.Equal(("dispute_not_found", 404), (refusal.Code, refusal.Status));
        await transactions.DidNotReceiveWithAnyArgs().ListAsync(default, default, default, default, default);
        Assert.Empty(meters.Of("audit.events"));
        Assert.Empty(meters.Of("security.events"));
    }

    [Fact]
    public async Task AMissingDisputeIsTheSameAnswerAndReadsAndAuditsNothing()
    {
        transactions.FindScopeAsync(Dispute, CancellationToken.None).Returns(Task.FromResult<AgentDisputeScope?>(null));

        NotFoundException refusal = await Assert.ThrowsAsync<NotFoundException>(
            () => queries.ListAsync(Dispute, new AgentCustomerTransactionsRequest(null, null), Agent, "trace", CancellationToken.None));

        Assert.Equal(("dispute_not_found", 404), (refusal.Code, refusal.Status));
        await transactions.DidNotReceiveWithAnyArgs().ListAsync(default, default, default, default, default);
        Assert.Empty(meters.Of("audit.events"));
    }

    private void GivenDispute(DisputeStatus status, DateTimeOffset disputedTransactionDate) =>
        transactions.FindScopeAsync(Dispute, CancellationToken.None)
            .Returns(Task.FromResult<AgentDisputeScope?>(new AgentDisputeScope(Customer, disputedTransactionDate, status)));
}
