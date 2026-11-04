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
