using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Eligibility;
using DisputePortal.Domain.Transactions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DisputePortal.UnitTests;

/// <summary>
/// A customer's dispute 404 is counted as bola_404 and an agent's never is. The helpers are tested apart, and so is the
/// customer command path the integration tests cannot reach (a dispute that was there a moment ago and is not any more).
/// </summary>
public sealed class DisputeNotFoundTests : IDisposable
{
    private static readonly DateTimeOffset Now = DisputeTestData.Now;

    private readonly MeterSandbox meters = new();
    private readonly IDisputeRepository disputes = Substitute.For<IDisputeRepository>();
    private readonly ITransactionRepository transactions = Substitute.For<ITransactionRepository>();
    private readonly DisputeCommands commands;

    public DisputeNotFoundTests()
    {
        FixedClock clock = new(Now);
        commands = new DisputeCommands(
            disputes,
            transactions,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<ICurrentCustomer>(),
            new DisputeEligibilityPolicy([], clock),
            clock,
            meters.Disputes,
            meters.Security,
            NullLogger<DisputeCommands>.Instance);
    }

    public void Dispose() => meters.Dispose();

    [Fact]
    public void ACustomersNotFoundIsCountedOnceAsBola404WithTheTypeAlone()
    {
        NotFoundException refusal = DisputeNotFound.ToCustomer(meters.Security);

        Assert.Equal(("dispute_not_found", 404), (refusal.Code, refusal.Status));
        MeterSandbox.Measured sample = Assert.Single(meters.Of("security.events"));
        Assert.Equal(1, sample.Value);
        KeyValuePair<string, object?> tag = Assert.Single(sample.Tags);
        Assert.Equal(("type", "bola_404"), (tag.Key, tag.Value));
    }

    // Same body for both callers, or the response would tell a customer which kind of caller they are not.
    [Fact]
    public void AnAgentsNotFoundIsTheSameAnswerAndIsNeverCounted()
    {
        NotFoundException agents = DisputeNotFound.ToAgent();
        NotFoundException customers = DisputeNotFound.ToCustomer(meters.Security);

        Assert.Equal((customers.Code, customers.Status, customers.Message), (agents.Code, agents.Status, agents.Message));
        Assert.Single(meters.Of("security.events"));
    }

    // The same id again names this customer's own dispute, which then cannot be read back: the owner filter answered, so it counts.
    [Fact]
    public async Task AReplayWhoseDisputeCannotBeReadBackIsCountedAsBola404()
    {
        Guid disputeId = Guid.NewGuid();
        Guid transactionId = Guid.NewGuid();
        transactions.GetSnapshotAsync(transactionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TransactionSnapshot?>(new TransactionSnapshot(TransactionStatus.Posted, TransactionDirection.Debit, Now.AddDays(-5), Money.Of(250.50m, "ZAR"))));
        disputes.FindTransactionIdAsync(disputeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Guid?>(transactionId));
        disputes.GetDetailAsync(disputeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<VersionedDispute?>(null));
        CreateDisputeRequest request = new(disputeId.ToString("D"), transactionId.ToString("D"), "duplicate_charge", "Charged twice for one order.", null);

        NotFoundException refusal = await Assert.ThrowsAsync<NotFoundException>(() => commands.SubmitAsync(request, DisputeTestData.CustomerSubject, CancellationToken.None));

        Assert.Equal("dispute_not_found", refusal.Code);
        Assert.Equal(["bola_404"], meters.SecurityEventTypes);
    }

    [Fact]
    public async Task AWithdrawalWhoseDisputeCannotBeReadBackIsCountedAsBola404()
    {
        Dispute dispute = DisputeTestData.Submitted();
        disputes.FindAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(dispute));
        disputes.GetDetailAsync(dispute.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<VersionedDispute?>(null));

        NotFoundException refusal = await Assert.ThrowsAsync<NotFoundException>(
            () => commands.WithdrawAsync(dispute.Id, DisputeETag.For(dispute.Version), DisputeTestData.CustomerSubject, CancellationToken.None));

        Assert.Equal("dispute_not_found", refusal.Code);
        Assert.Equal(["bola_404"], meters.SecurityEventTypes);
    }

    [Fact]
    public async Task AWithdrawalOfAMissingDisputeIsCountedOnceNotTwice()
    {
        disputes.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Dispute?>(null));

        await Assert.ThrowsAsync<NotFoundException>(() => commands.WithdrawAsync(Guid.NewGuid(), "\"1\"", DisputeTestData.CustomerSubject, CancellationToken.None));

        Assert.Equal(["bola_404"], meters.SecurityEventTypes);
    }
}
