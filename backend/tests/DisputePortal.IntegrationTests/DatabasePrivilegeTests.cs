using DisputePortal.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>(e) The app role is DML-least-privilege: reads customers, reads and adds key-ring rows, and nothing else.</summary>
[Collection("api")]
public sealed class DatabasePrivilegeTests(ApiFactory api)
{
    private const string InsufficientPrivilege = "42501";

    [Theory]
    [InlineData("INSERT INTO customers (id, keycloak_subject, display_name) VALUES (gen_random_uuid(), 'intruder', 'Intruder')")]
    [InlineData("UPDATE customers SET display_name = 'Changed'")]
    [InlineData("DELETE FROM customers")]
    // Nor lock a customer row: FOR UPDATE needs UPDATE, which is why the customer lock is an advisory one (ADR-014).
    [InlineData("SELECT id FROM customers LIMIT 1 FOR UPDATE")]
    [InlineData("UPDATE data_protection_keys SET xml = NULL")]
    [InlineData("DELETE FROM data_protection_keys")]
    [InlineData("SELECT count(*) FROM \"__EFMigrationsHistory\"")]
    [InlineData("CREATE TABLE intruder (id int)")]
    [InlineData("TRUNCATE customers")]
    [InlineData("INSERT INTO accounts (id, customer_id, masked_number, currency) VALUES (gen_random_uuid(), 'c0570000-0000-4000-8000-000000000001', '**** 0000', 'ZAR')")]
    [InlineData("UPDATE accounts SET masked_number = '**** 0000'")]
    [InlineData("DELETE FROM accounts")]
    [InlineData("INSERT INTO transactions (id, account_id, customer_id, reference, merchant_name, category, amount, currency, direction, status, transaction_date, posted_at) SELECT gen_random_uuid(), account_id, customer_id, 'X' || reference, merchant_name, category, amount, currency, direction, status, transaction_date, posted_at FROM transactions LIMIT 1")]
    [InlineData("UPDATE transactions SET amount = 1")]
    [InlineData("DELETE FROM transactions")]
    [InlineData("TRUNCATE transactions")]
    [InlineData("INSERT INTO dispute_reasons (code, label, description, sort_order) VALUES ('x', 'x', 'x', 99)")]
    [InlineData("UPDATE dispute_reasons SET label = 'Changed'")]
    [InlineData("DELETE FROM dispute_reasons")]
    // A dispute's identity, owner, subject and amount are fixed once submitted; only its lifecycle columns move.
    [InlineData("UPDATE disputes SET customer_id = customer_id")]
    [InlineData("UPDATE disputes SET transaction_id = transaction_id")]
    [InlineData("UPDATE disputes SET description = 'Rewritten'")]
    [InlineData("UPDATE disputes SET disputed_amount = 1")]
    [InlineData("UPDATE disputes SET created_at = now()")]
    [InlineData("DELETE FROM disputes")]
    [InlineData("TRUNCATE disputes")]
    // The history is append-only, and its time comes from the database alone.
    [InlineData("INSERT INTO dispute_status_changes (dispute_id, to_status, actor_type, actor_id, occurred_at) SELECT id, 'Submitted', 'Customer', 'x', now() - interval '1 day' FROM disputes LIMIT 1")]
    [InlineData("INSERT INTO dispute_status_changes (id, dispute_id, to_status, actor_type, actor_id) SELECT 1, id, 'Submitted', 'Customer', 'x' FROM disputes LIMIT 1")]
    [InlineData("UPDATE dispute_status_changes SET note = 'Rewritten'")]
    [InlineData("UPDATE dispute_status_changes SET occurred_at = now()")]
    [InlineData("DELETE FROM dispute_status_changes")]
    [InlineData("TRUNCATE dispute_status_changes")]
    public async Task TheAppRoleCannotWriteOrAlterWhatItShouldNot(string sql)
    {
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsAppAsync(sql));

        Assert.Equal(InsufficientPrivilege, refused.SqlState);
    }

    // The read model is readable by the app (the Owner filter does the scoping in EF), never writable.
    [Fact]
    public async Task TheAppRoleCanReadAccountsAndTransactions()
    {
        await using NpgsqlConnection connection = new(api.AppConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand read = new("SELECT (SELECT count(*) FROM accounts) + (SELECT count(*) FROM transactions)", connection);

        Assert.True((long)(await read.ExecuteScalarAsync())! > 0);
    }

    // What the Disputes slice does, as dispute_app, in a transaction rolled back so nothing reaches the shared database.
    [Fact]
    public async Task TheAppRoleCanSubmitAndMoveADisputeAndTheDatabaseStampsTheHistory()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        Guid transactionId = await customer.AddEligibleTransactionAsync(api);
        await using NpgsqlConnection connection = new(api.AppConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using NpgsqlCommand insert = new("""
            INSERT INTO disputes (id, transaction_id, customer_id, reason_code, description, disputed_amount, currency, status, created_at, updated_at)
            VALUES (@id, @transaction, @customer, 'other', 'Probe dispute.', 1, 'ZAR', 'Submitted', now(), now());
            INSERT INTO dispute_status_changes (dispute_id, from_status, to_status, actor_type, actor_id, note)
            VALUES (@id, NULL, 'Submitted', 'Customer', 'probe', NULL);
            UPDATE disputes SET status = 'Withdrawn', updated_at = now(), resolved_at = now() WHERE id = @id;
            SELECT occurred_at FROM dispute_status_changes WHERE dispute_id = @id;
            """, connection, transaction);
        Guid id = Guid.NewGuid();
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("transaction", transactionId);
        insert.Parameters.AddWithValue("customer", customer.CustomerId);

        object? occurredAt = await insert.ExecuteScalarAsync();
        await transaction.RollbackAsync();

        Assert.IsType<DateTime>(occurredAt);
        Assert.Equal(0, await AdminSql.CountAsync(api.AdminConnectionString, "SELECT count(*) FROM disputes WHERE id = @id", ("id", id)));
    }

    [Fact]
    public async Task TheAppRoleCanReadCustomersAndAddADataProtectionKey()
    {
        await using NpgsqlConnection connection = new(api.AppConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using NpgsqlCommand read = new("SELECT count(*) FROM customers", connection, transaction);
        await using NpgsqlCommand insert = new("INSERT INTO data_protection_keys (friendly_name, xml) VALUES ('probe', '<key/>')", connection, transaction);

        long customers = (long)(await read.ExecuteScalarAsync())!;
        int inserted = await insert.ExecuteNonQueryAsync();
        // Rolled back: a junk key must never reach the shared key ring.
        await transaction.RollbackAsync();

        Assert.True(customers >= Seeder.Customers.Count);
        Assert.Equal(1, inserted);
    }

    // The customer lock of ADR-014: roles.sql revokes no function from PUBLIC, so the app role may take it.
    [Fact]
    public async Task TheAppRoleCanTakeTheTransactionScopedCustomerLock()
    {
        await using NpgsqlConnection connection = new(api.AppConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using NpgsqlCommand take = new("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        take.Parameters.AddWithValue("key", Guid.NewGuid().ToString("D"));
        await using NpgsqlCommand count = new(
            "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND granted AND pid = pg_backend_pid()", connection, transaction);

        await take.ExecuteNonQueryAsync();
        long held = (long)(await count.ExecuteScalarAsync())!;
        await transaction.RollbackAsync();

        Assert.Equal(1, held);
    }

    [Fact]
    public async Task MigrationsSeededBothCustomersAndADataProtectionKey()
    {
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();

        // Other tests add their own customers to the shared database; the seeded two must be among them.
        List<string> subjects = await db.Customers.Select(customer => customer.KeycloakSubject).ToListAsync();

        Assert.Contains(Seeder.ThandiSubject, subjects);
        Assert.Contains(Seeder.SiphoSubject, subjects);
        Assert.NotEmpty(await db.DataProtectionKeys.ToListAsync());
    }

    // Compares the model with the migrations' snapshot; the app role cannot even read the migrations history.
    [Fact]
    public async Task TheModelHasNoChangesMissingFromTheMigrations()
    {
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    private async Task ExecuteAsAppAsync(string sql)
    {
        await using NpgsqlConnection connection = new(api.AppConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
