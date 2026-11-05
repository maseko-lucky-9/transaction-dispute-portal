using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Disputes;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// What the database guarantees whoever writes: the Owner filter authorises on customer_id alone, and the composite
/// foreign keys keep that column equal to the parent's customer; the partial unique index and CHECKs back the dispute rules.
/// </summary>
[Collection("api")]
public sealed class ReadModelConstraintTests(ApiFactory api)
{
    private const string ForeignKeyViolation = "23503";
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

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

    private const string InsertDisputeSql = """
        INSERT INTO disputes (id, transaction_id, customer_id, reason_code, description, disputed_amount, currency, status, created_at, updated_at, resolved_at)
        VALUES (gen_random_uuid(), @transaction, @customer, 'other', 'Probe dispute.', 1, 'ZAR', @status, now(), now(),
                CASE WHEN @status IN ('Accepted', 'Rejected', 'Withdrawn') THEN now() END)
        """;

    // The backstop behind the application's check: even the owner cannot make a second active dispute on a transaction.
    [Fact]
    public async Task ASecondActiveDisputeOnOneTransactionIsRefusedByThePartialUniqueIndex()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid transactionId = await customer.AddEligibleTransactionAsync(api);
        await using NpgsqlConnection connection = new(api.OwnerConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        int first = await InsertDisputeAsync(connection, transaction, transactionId, customer.CustomerId, "Submitted");
        int withdrawn = await InsertDisputeAsync(connection, transaction, transactionId, customer.CustomerId, "Withdrawn");
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => InsertDisputeAsync(connection, transaction, transactionId, customer.CustomerId, "UnderReview"));
        await transaction.RollbackAsync();

        Assert.Equal((1, 1), (first, withdrawn));
        Assert.Equal(UniqueViolation, refused.SqlState);
        Assert.Equal(DisputeConfiguration.OneActivePerTransactionIndex, refused.ConstraintName);
    }

    [Fact]
    public async Task ADisputeCannotNameAnotherCustomersTransaction()
    {
        TestCustomer owner = await TestCustomer.CreateAsync(api);
        Guid transactionId = await owner.AddEligibleTransactionAsync(api);
        await using NpgsqlConnection connection = new(api.OwnerConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => InsertDisputeAsync(connection, transaction, transactionId, Seeder.SiphoCustomerId, "Submitted"));
        await transaction.RollbackAsync();

        Assert.Equal(ForeignKeyViolation, refused.SqlState);
        Assert.Equal("fk_disputes_transactions_transaction_id_customer_id", refused.ConstraintName);
    }

    // resolved_at is set exactly when the dispute is terminal.
    [Theory]
    [InlineData("UPDATE disputes SET resolved_at = now() WHERE id = @id")]
    [InlineData("UPDATE disputes SET status = 'Withdrawn' WHERE id = @id")]
    [InlineData("UPDATE disputes SET status = 'Accepted', resolved_at = now() WHERE id = @id")]
    public async Task TheDisputeChecksHoldEvenForTheOwner(string sql)
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid disputeId = await customer.AddDisputeAsync(api, await customer.AddEligibleTransactionAsync(api), DisputeStatus.Submitted);
        await using NpgsqlConnection connection = new(api.OwnerConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand update = new(sql, connection);
        update.Parameters.AddWithValue("id", disputeId);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());

        Assert.Equal(CheckViolation, refused.SqlState);
    }

    private static async Task<int> InsertDisputeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid transactionId, Guid customerId, string status)
    {
        await using NpgsqlCommand insert = new(InsertDisputeSql, connection, transaction);
        insert.Parameters.AddWithValue("transaction", transactionId);
        insert.Parameters.AddWithValue("customer", customerId);
        insert.Parameters.AddWithValue("status", status);
        return await insert.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, Guid customer)
    {
        await using NpgsqlCommand insert = new(InsertSql, connection, transaction);
        insert.Parameters.AddWithValue("account", account);
        insert.Parameters.AddWithValue("customer", customer);
        return await insert.ExecuteNonQueryAsync();
    }
}
