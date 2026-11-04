using DisputePortal.Api.Features.Transactions;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.UnitTests;

public sealed class TransactionFilterTests
{
    [Fact]
    public void TheToDayIsInclusive()
    {
        TransactionFilter filter = new(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), null);

        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), filter.FromInclusive);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), filter.ToExclusive);
    }

    [Fact]
    public void ParsingIsCaseInsensitiveOnStatus() =>
        Assert.Equal(TransactionStatus.Reversed, TransactionFilter.Parse(new TransactionListRequest(null, null, null, null, "reversed")).Status);

    [Fact]
    public void EqualFiltersShareAFingerprintAndAnyChangeAltersIt()
    {
        TransactionFilter filter = new(new DateOnly(2026, 3, 1), null, TransactionStatus.Posted);

        Assert.Equal(filter.Fingerprint(), new TransactionFilter(new DateOnly(2026, 3, 1), null, TransactionStatus.Posted).Fingerprint());
        Assert.NotEqual(filter.Fingerprint(), (filter with { From = new DateOnly(2026, 3, 2) }).Fingerprint());
        Assert.NotEqual(filter.Fingerprint(), (filter with { To = new DateOnly(2026, 3, 9) }).Fingerprint());
        Assert.NotEqual(filter.Fingerprint(), (filter with { Status = null }).Fingerprint());
    }
}
