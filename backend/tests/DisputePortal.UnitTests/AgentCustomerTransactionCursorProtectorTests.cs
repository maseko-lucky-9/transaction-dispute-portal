using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Paging;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.UnitTests;

public sealed class AgentCustomerTransactionCursorProtectorTests
{
    private const string Agent = "agent-1";
    private static readonly Guid Dispute = Guid.NewGuid();
    private static readonly TransactionKey Last = new(new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.Zero).AddTicks(1230), Guid.NewGuid());

    private readonly AgentCustomerTransactionCursorProtector cursors = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void ACursorRoundTripsToTheSameKey() => Assert.Equal(Last, cursors.Unprotect(cursors.Protect(Last, Agent, Dispute), Agent, Dispute));

    [Fact]
    public void ACursorHoldsNoSubjectOrIdText()
    {
        string cursor = cursors.Protect(Last, Agent, Dispute);

        Assert.DoesNotContain(Agent, cursor, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Last.Id.ToString("N"), cursor, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Dispute.ToString("N"), cursor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnotherAgentsCursorIsRefused() =>
        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursors.Protect(Last, Agent, Dispute), "agent-2", Dispute));

    [Fact]
    public void AnotherDisputesCursorIsRefused() =>
        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursors.Protect(Last, Agent, Dispute), Agent, Guid.NewGuid()));

    [Fact]
    public void AQueueCursorIsNotACustomerTransactionsCursor()
    {
        AgentCursorProtector queueCursors = new(new EphemeralDataProtectionProvider());
        string foreign = queueCursors.Protect(new DisputeKey(Last.TransactionDate, Last.Id), Agent, new AgentQueueFilter(null));

        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(foreign, Agent, Dispute));
    }

    [Fact]
    public void ACustomersTransactionCursorIsNotAnAgentsCursor()
    {
        TransactionCursorProtector customerCursors = new(new EphemeralDataProtectionProvider());
        string foreign = customerCursors.Protect(Last, Guid.NewGuid(), new TransactionFilter(null, null, null));

        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(foreign, Agent, Dispute));
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("!!!")]
    public void GarbageIsRefused(string cursor) => Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursor, Agent, Dispute));

    [Fact]
    public void ATamperedCursorIsRefused()
    {
        char[] cursor = cursors.Protect(Last, Agent, Dispute).ToCharArray();
        int middle = cursor.Length / 2;
        cursor[middle] = cursor[middle] == 'A' ? 'B' : 'A';

        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(new string(cursor), Agent, Dispute));
    }

    // A literal on purpose: the fingerprint is what binds a cursor to its dispute, so a change to its canonical form must be a decision.
    [Fact]
    public void TheFingerprintIsStable() =>
        Assert.Equal("B2FEC5B17185DB2FC7D25D147208E19F", AgentCustomerTransactionCursorProtector.Fingerprint(Guid.Parse("11111111-2222-3333-4444-555555555555")));

    [Fact]
    public void ADifferentDisputeGivesADifferentFingerprint() =>
        Assert.NotEqual(AgentCustomerTransactionCursorProtector.Fingerprint(Dispute), AgentCustomerTransactionCursorProtector.Fingerprint(Guid.NewGuid()));

    [Fact]
    public void TheFingerprintIsTheDisputeIdInTheDashedForm() =>
        Assert.Equal(
            KeysetCursor.Fingerprint("agent-customer-transactions;dispute=11111111-2222-3333-4444-555555555555"),
            AgentCustomerTransactionCursorProtector.Fingerprint(Guid.Parse("11111111-2222-3333-4444-555555555555")));
}
