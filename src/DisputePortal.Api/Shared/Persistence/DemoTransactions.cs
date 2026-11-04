using DisputePortal.Api.Features.Transactions;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// Deterministic demo transactions for one seeded customer, dated relative to <c>now</c>: mostly posted debits inside
/// the 120-day window, plus credits, pending, reversed and about 12% older than the window. A fixed seed per customer
/// keeps every run (and every test) identical for the same clock.
/// </summary>
public static class DemoTransactions
{
    public const int PerCustomer = 75;
    public const int OlderThanWindow = 9;
    public const int Pending = 6;
    public const int Reversed = 4;
    public const int PostedCredits = 8;
    public const decimal MinAmount = 5.00m;
    public const decimal MaxAmount = 4500.00m;

    private const int Seed = 20261104;
    private const int SpreadDays = 150;
    private const int WindowDays = 120;

    private static readonly (string Merchant, string Category, decimal Min, decimal Max)[] Debits =
    [
        ("Woolworths Food", "Groceries", 80, 1800), ("Checkers Hyper", "Groceries", 60, 2200), ("Pick n Pay", "Groceries", 40, 1500),
        ("Engen", "Fuel", 300, 1400), ("Shell", "Fuel", 250, 1300), ("Takealot.com", "Online shopping", 99, 4500),
        ("Uber", "Transport", 45, 320), ("Uber Eats", "Takeaways", 85, 450), ("Mr D Food", "Takeaways", 90, 380),
        ("Netflix", "Subscriptions", 99, 199), ("Showmax", "Subscriptions", 49, 99), ("Discovery Health", "Medical aid", 1800, 4200),
        ("Vodacom", "Mobile", 199, 899), ("MTN", "Mobile", 149, 699), ("Dis-Chem", "Pharmacy", 55, 950), ("Clicks", "Pharmacy", 35, 750),
        ("Spur Steak Ranches", "Restaurants", 180, 900), ("City of Johannesburg", "Utilities", 650, 2400),
        ("Builders Warehouse", "Home", 120, 3800), ("Computicket", "Entertainment", 150, 1200),
    ];

    private static readonly (string Merchant, string Category, decimal Min, decimal Max)[] Credits =
    [
        ("Refund - Takealot.com", "Refund", 99, 1500), ("Transfer from savings", "Transfer", 500, 4500),
        ("Discovery Vitality cashback", "Rewards", 25, 400), ("Interest", "Interest", 5, 60),
    ];

    private enum Kind
    {
        Old,
        Pending,
        Reversed,
        Credit,
        Debit,
    }

    public static IReadOnlyList<Transaction> For(int customerOrdinal, Guid customerId, IReadOnlyList<Account> accounts, DateTimeOffset now)
    {
        Random random = new(Seed + customerOrdinal);
        List<Transaction> transactions = new(PerCustomer);
        foreach ((Kind kind, int index) in Plan().Select((kind, index) => (kind, index)))
        {
            transactions.Add(Create(random, kind, index, customerOrdinal, customerId, accounts[index % accounts.Count], now));
        }

        return transactions;
    }

    private static IEnumerable<Kind> Plan() =>
        Enumerable.Repeat(Kind.Old, OlderThanWindow)
            .Concat(Enumerable.Repeat(Kind.Pending, Pending))
            .Concat(Enumerable.Repeat(Kind.Reversed, Reversed))
            .Concat(Enumerable.Repeat(Kind.Credit, PostedCredits))
            .Concat(Enumerable.Repeat(Kind.Debit, PerCustomer - OlderThanWindow - Pending - Reversed - PostedCredits));

    private static Transaction Create(Random random, Kind kind, int index, int customerOrdinal, Guid customerId, Account account, DateTimeOffset now)
    {
        int daysAgo = kind switch
        {
            Kind.Old => random.Next(WindowDays + 1, SpreadDays + 1),
            Kind.Pending => random.Next(0, 4),
            _ => random.Next(0, WindowDays),
        };
        // Earlier in the day than now, never later, so no transaction lies in the future.
        DateTimeOffset date = now.AddDays(-daysAgo).AddMinutes(-random.Next(1, 600));
        TransactionDirection direction = kind == Kind.Credit ? TransactionDirection.Credit : TransactionDirection.Debit;
        (string merchant, string category, decimal min, decimal max) = Pick(random, direction == TransactionDirection.Credit ? Credits : Debits);
        TransactionStatus status = kind switch
        {
            Kind.Pending => TransactionStatus.Pending,
            Kind.Reversed => TransactionStatus.Reversed,
            _ => TransactionStatus.Posted,
        };
        return new Transaction
        {
            Id = NewGuid(random),
            AccountId = account.Id,
            CustomerId = customerId,
            Reference = $"FT{customerOrdinal}{index:D3}{random.Next(100_000, 1_000_000)}",
            MerchantName = merchant,
            Category = category,
            Amount = Math.Clamp(Math.Round(min + ((decimal)random.NextDouble() * (max - min)), 2), MinAmount, MaxAmount),
            Currency = account.Currency,
            Direction = direction,
            Status = status,
            TransactionDate = date,
            PostedAt = status == TransactionStatus.Pending ? null : Min(date.AddHours(random.Next(1, 48)), now),
        };
    }

    private static (string, string, decimal, decimal) Pick(Random random, (string, string, decimal, decimal)[] choices) =>
        choices[random.Next(choices.Length)];

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    // A version-4 shape from the seeded generator, so ids are stable across runs.
    private static Guid NewGuid(Random random)
    {
        byte[] bytes = new byte[16];
        random.NextBytes(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
