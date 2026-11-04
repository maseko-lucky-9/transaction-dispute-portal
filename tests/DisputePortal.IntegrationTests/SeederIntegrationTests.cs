using DisputePortal.Api.Shared.Persistence;

namespace DisputePortal.IntegrationTests;

/// <summary>The real --migrate path (migrations, grants, seeding) on a fresh database with a fixed clock, run twice.</summary>
[Collection("api")]
public sealed class SeederIntegrationTests(ApiFactory api)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeedingIsIdempotentAndDatedRelativeToTheInjectedClock()
    {
        (string database, string username, string password) = await api.CreateEmptyDatabaseAsync();
        string admin = api.AdminConnectionStringFor(database);

        Assert.Equal(0, await api.MigrateAsync(database, username, password, new SettableClock(Now)));
        List<(Guid Customer, long Count)> first = await CountsAsync(admin);
        Assert.Equal(0, await api.MigrateAsync(database, username, password, new SettableClock(Now.AddDays(3))));
        List<(Guid Customer, long Count)> second = await CountsAsync(admin);

        Assert.Equal([(Seeder.ThandiCustomerId, 75L), (Seeder.SiphoCustomerId, 75L)], first.OrderBy(row => row.Customer == Seeder.SiphoCustomerId));
        Assert.Equal(first, second);
        Assert.Equal(3, await AdminSql.CountAsync(admin, "SELECT count(*) FROM accounts"));
        Assert.Equal(2, await AdminSql.CountAsync(admin, "SELECT count(*) FROM customers"));
        Assert.Equal(0, await AdminSql.CountAsync(admin, "SELECT count(*) FROM transactions WHERE transaction_date > @now OR transaction_date < @earliest",
            ("now", Now), ("earliest", Now.AddDays(-152))));
        Assert.Equal(18, await AdminSql.CountAsync(admin, "SELECT count(*) FROM transactions WHERE transaction_date::date < (@now::timestamptz AT TIME ZONE 'UTC')::date - 120",
            ("now", Now)));
        Assert.Equal(new[] { "Credit", "Debit" }, await AdminSql.ListAsync(admin, "SELECT DISTINCT direction FROM transactions ORDER BY 1", reader => reader.GetString(0)));
        Assert.Equal(new[] { "Pending", "Posted", "Reversed" }, await AdminSql.ListAsync(admin, "SELECT DISTINCT status FROM transactions ORDER BY 1", reader => reader.GetString(0)));
    }

    private static Task<List<(Guid Customer, long Count)>> CountsAsync(string admin) =>
        AdminSql.ListAsync(admin, "SELECT customer_id, count(*) FROM transactions GROUP BY customer_id ORDER BY customer_id",
            reader => (reader.GetGuid(0), reader.GetInt64(1)));
}
