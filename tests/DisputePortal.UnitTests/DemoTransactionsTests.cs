using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.UnitTests;

public sealed class DemoTransactionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 30, 0, TimeSpan.Zero);

    private static IReadOnlyList<Transaction> Thandi() =>
        DemoTransactions.For(1, Seeder.ThandiCustomerId, [.. Seeder.Accounts.Where(account => account.CustomerId == Seeder.ThandiCustomerId)], Now);

    [Fact]
    public void ACustomerGets75TransactionsWithTheIntendedMix()
    {
        IReadOnlyList<Transaction> transactions = Thandi();

        Assert.Equal(75, transactions.Count);
        Assert.Equal(6, transactions.Count(transaction => transaction.Status == TransactionStatus.Pending));
        Assert.Equal(4, transactions.Count(transaction => transaction.Status == TransactionStatus.Reversed));
        Assert.Equal(8, transactions.Count(transaction => transaction.Direction == TransactionDirection.Credit));
        Assert.Equal(9, transactions.Count(transaction => AgeDays(transaction) > 120));
    }

    [Fact]
    public void DatesAreRelativeToTheClockAndNeverInTheFuture()
    {
        IReadOnlyList<Transaction> transactions = Thandi();

        Assert.All(transactions, transaction => Assert.InRange(transaction.TransactionDate, Now.AddDays(-152), Now));
        Assert.All(transactions, transaction => Assert.True(transaction.PostedAt is null || (transaction.PostedAt >= transaction.TransactionDate && transaction.PostedAt <= Now)));
        Assert.All(transactions, transaction => Assert.Equal(transaction.Status == TransactionStatus.Pending, transaction.PostedAt is null));
    }

    [Fact]
    public void AmountsArePositiveWithAtMostTwoDecimals() =>
        Assert.All(Thandi(), transaction =>
        {
            Assert.InRange(transaction.Amount, 5.00m, 4500.00m);
            Assert.Equal(transaction.Amount, decimal.Round(transaction.Amount, 2));
        });

    [Fact]
    public void TheSameClockGivesTheSameData() =>
        Assert.Equal(Thandi().Select(Fingerprint), Thandi().Select(Fingerprint));

    [Fact]
    public void ReferencesAndIdsAreUniqueAcrossCustomers()
    {
        IReadOnlyList<Transaction> sipho = DemoTransactions.For(2, Seeder.SiphoCustomerId, [.. Seeder.Accounts.Where(account => account.CustomerId == Seeder.SiphoCustomerId)], Now);
        List<Transaction> all = [.. Thandi(), .. sipho];

        Assert.Equal(all.Count, all.Select(transaction => transaction.Reference).Distinct().Count());
        Assert.Equal(all.Count, all.Select(transaction => transaction.Id).Distinct().Count());
        Assert.All(sipho, transaction => Assert.Equal(Seeder.SiphoCustomerId, transaction.CustomerId));
    }

    [Fact]
    public void EachTransactionBelongsToOneOfItsCustomersAccounts()
    {
        HashSet<Guid> accounts = [.. Seeder.Accounts.Where(account => account.CustomerId == Seeder.ThandiCustomerId).Select(account => account.Id)];

        Assert.All(Thandi(), transaction => Assert.Contains(transaction.AccountId, accounts));
    }

    private static int AgeDays(Transaction transaction) =>
        DateOnly.FromDateTime(Now.UtcDateTime).DayNumber - DateOnly.FromDateTime(transaction.TransactionDate.UtcDateTime).DayNumber;

    private static string Fingerprint(Transaction transaction) =>
        $"{transaction.Id}|{transaction.Reference}|{transaction.Amount}|{transaction.Status}|{transaction.TransactionDate:O}";
}
