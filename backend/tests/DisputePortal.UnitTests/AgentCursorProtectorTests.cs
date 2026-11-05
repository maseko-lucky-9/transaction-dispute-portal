using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Paging;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.UnitTests;

public sealed class AgentCursorProtectorTests
{
    private const string Agent = "agent-1";
    private static readonly AgentQueueFilter Open = new((DisputeStatus?)null);
    private static readonly DisputeKey Last = new(new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.Zero).AddTicks(1230), Guid.NewGuid());

    private readonly AgentCursorProtector cursors = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void ACursorRoundTripsToTheSameKey() => Assert.Equal(Last, cursors.Unprotect(cursors.Protect(Last, Agent, Open), Agent, Open));

    [Fact]
    public void ACursorHoldsNoSubjectOrIdText()
    {
        string cursor = cursors.Protect(Last, Agent, Open);

        Assert.DoesNotContain(Agent, cursor, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Last.Id.ToString("N"), cursor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnotherAgentsCursorIsRefused() =>
        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursors.Protect(Last, Agent, Open), "agent-2", Open));

    [Fact]
    public void ACursorUsedWithAnotherStatusFilterIsRefused() =>
        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursors.Protect(Last, Agent, Open), Agent, new AgentQueueFilter(DisputeStatus.Accepted)));

    [Fact]
    public void ADifferentStatusGivesADifferentFingerprint() =>
        Assert.NotEqual(Open.Fingerprint(), new AgentQueueFilter(DisputeStatus.Submitted).Fingerprint());

    [Fact]
    public void ACustomerDisputeCursorIsNotAnAgentCursor()
    {
        DisputeCursorProtector customerCursors = new(new EphemeralDataProtectionProvider());
        string foreign = customerCursors.Protect(Last, Guid.NewGuid(), new DisputeListFilter(null));

        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(foreign, Agent, Open));
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("!!!")]
    public void GarbageIsRefused(string cursor) => Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursor, Agent, Open));

    [Fact]
    public void ATamperedCursorIsRefused()
    {
        char[] cursor = cursors.Protect(Last, Agent, Open).ToCharArray();
        int middle = cursor.Length / 2;
        cursor[middle] = cursor[middle] == 'A' ? 'B' : 'A';

        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(new string(cursor), Agent, Open));
    }

    [Fact]
    public void KeysetCursorBindsScopeAndFingerprintSeparately()
    {
        KeysetCursor keyset = new(new EphemeralDataProtectionProvider().CreateProtector("test"));
        string cursor = keyset.Protect(Last.CreatedAt, Last.Id, "scope-a", "fp-a");

        Assert.Equal((Last.CreatedAt, Last.Id), keyset.Unprotect(cursor, "scope-a", "fp-a"));
        Assert.Throws<InvalidCursorException>(() => keyset.Unprotect(cursor, "scope-b", "fp-a"));
        Assert.Throws<InvalidCursorException>(() => keyset.Unprotect(cursor, "scope-a", "fp-b"));
    }
}
