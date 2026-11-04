using DisputePortal.Api.Shared.Persistence;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The Owner filter authorises on transactions.customer_id alone; the composite foreign key (account_id, customer_id)
/// is what keeps that column equal to the account's customer, even for a writer that is not the app.
/// </summary>
[Collection("api")]
public sealed class ReadModelConstraintTests(ApiFactory api)
{
    private const string ForeignKeyViolation = "23503";

    private const string InsertSql = """
        INSERT INTO transactions (id, account_id, customer_id, reference, merchant_name, category, amount, currency, direction, status, transaction_date, posted_at)
        VALUES (gen_random_uuid(), @account, @customer, 'K' || left(replace(gen_random_uuid()::text, '-', ''), 20), 'Probe', 'Test', 1, 'ZAR', 'Debit', 'Pending', now(), NULL)
        """;

    [Fact]
    public async Task ATransactionCannotPointAtAnotherCustomersAccount()
    {
        Guid thandisAccount = Seeder.Accounts.First(account => account.CustomerId == Seeder.ThandiCustomerId).Id;
        await using NpgsqlConnection connection = new(api.OwnerConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        // The control: the same row with the account's own customer is accepted, so the refusal below is the mismatch.
        int accepted = await InsertAsync(connection, transaction, thandisAccount, Seeder.ThandiCustomerId);
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAsync(connection, transaction, thandisAccount, Seeder.SiphoCustomerId));
        // Rolled back: probe rows must never reach the shared database.
        await transaction.RollbackAsync();

        Assert.Equal(1, accepted);
        Assert.Equal(ForeignKeyViolation, refused.SqlState);
        Assert.Equal("fk_transactions_accounts_account_id_customer_id", refused.ConstraintName);
    }

    private static async Task<int> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, Guid customer)
    {
        await using NpgsqlCommand insert = new(InsertSql, connection, transaction);
        insert.Parameters.AddWithValue("account", account);
        insert.Parameters.AddWithValue("customer", customer);
        return await insert.ExecuteNonQueryAsync();
    }
}
