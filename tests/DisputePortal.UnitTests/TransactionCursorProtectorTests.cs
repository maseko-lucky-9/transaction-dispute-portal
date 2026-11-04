using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Domain.Transactions;
using Microsoft.AspNetCore.DataProtection;

namespace DisputePortal.UnitTests;

public sealed class TransactionCursorProtectorTests
{
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly TransactionFilter Filter = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), TransactionStatus.Posted);
    private static readonly TransactionKey Last = new(new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.Zero).AddTicks(1230), Guid.NewGuid());

    private readonly TransactionCursorProtector cursors = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void ACursorRoundTripsToTheSameKey() =>
        Assert.Equal(Last, cursors.Unprotect(cursors.Protect(Last, Customer, Filter), Customer, Filter));

    [Fact]
    public void ACursorIsOpaque()
    {
        string cursor = cursors.Protect(Last, Customer, Filter);

        Assert.DoesNotContain(Last.Id.ToString("N"), cursor, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Customer.ToString("N"), cursor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnotherCustomersCursorIsRefused() =>
        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursors.Protect(Last, Customer, Filter), Guid.NewGuid(), Filter));

    [Fact]
    public void ACursorUsedWithOtherFiltersIsRefused() =>
        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursors.Protect(Last, Customer, Filter), Customer, Filter with { Status = TransactionStatus.Pending }));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("!!!")]
    public void GarbageIsRefused(string cursor) =>
        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(cursor, Customer, Filter));

    [Fact]
    public void ATamperedCursorIsRefused()
    {
        char[] cursor = cursors.Protect(Last, Customer, Filter).ToCharArray();
        int middle = cursor.Length / 2;
        cursor[middle] = cursor[middle] == 'A' ? 'B' : 'A';

        Assert.Throws<InvalidCursorException>(() => cursors.Unprotect(new string(cursor), Customer, Filter));
    }

    [Fact]
    public void AnotherKeyRingsCursorIsRefused() =>
        Assert.Throws<InvalidCursorException>(() =>
            new TransactionCursorProtector(new EphemeralDataProtectionProvider()).Unprotect(cursors.Protect(Last, Customer, Filter), Customer, Filter));
}
