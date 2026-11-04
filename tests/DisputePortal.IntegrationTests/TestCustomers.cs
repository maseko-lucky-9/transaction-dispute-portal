using DisputePortal.Api.Shared.Security;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// Customers with hand-picked transactions, written as the superuser (the app role cannot write the read model), and
/// signed in through the fake IdP like any other customer.
/// </summary>
public sealed record TestCustomer(IdpUser User, Guid CustomerId, Guid AccountId)
{
    private const string InsertCustomerSql = """
        INSERT INTO customers (id, keycloak_subject, display_name) VALUES (@customer, @subject, @name);
        INSERT INTO accounts (id, customer_id, masked_number, currency) VALUES (@account, @customer, '**** 0001', 'ZAR');
        """;

    private const string InsertTransactionSql = """
        INSERT INTO transactions (id, account_id, customer_id, reference, merchant_name, category, amount, currency, direction, status, transaction_date, posted_at)
        VALUES (@id, @account, @customer, @reference, 'Test merchant', 'Test', @amount, 'ZAR', @direction, @status, @date, @posted)
        """;

    // Written as the superuser, past the app's grants: a dispute in any status, with its first history entry.
    private const string InsertDisputeSql = """
        INSERT INTO disputes (id, transaction_id, customer_id, reason_code, description, disputed_amount, currency, status, created_at, updated_at, resolved_at, resolution_note)
        VALUES (@id, @transaction, @customer, 'other', 'Inserted by a test.', 1, 'ZAR', @status, @created, @created,
                CASE WHEN @status IN ('Accepted', 'Rejected', 'Withdrawn') THEN now() END,
                CASE WHEN @status IN ('Accepted', 'Rejected') THEN 'Decided by a test.' END);
        INSERT INTO dispute_status_changes (dispute_id, from_status, to_status, actor_type, actor_id)
        VALUES (@id, NULL, 'Submitted', 'Customer', @subject);
        """;

    public static async Task<TestCustomer> CreateAsync(ApiFactory api)
    {
        TestCustomer customer = new(new IdpUser("test-" + Guid.NewGuid().ToString("N"), "Test Customer", [Roles.Customer]), Guid.NewGuid(), Guid.NewGuid());
        await using NpgsqlConnection connection = new(api.AdminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(InsertCustomerSql, connection);
        command.Parameters.AddWithValue("customer", customer.CustomerId);
        command.Parameters.AddWithValue("account", customer.AccountId);
        command.Parameters.AddWithValue("subject", customer.User.Subject);
        command.Parameters.AddWithValue("name", customer.User.Name);
        await command.ExecuteNonQueryAsync();
        return customer;
    }

    public async Task<Guid> AddTransactionAsync(ApiFactory api, TransactionStatus status, TransactionDirection direction, DateTimeOffset date)
    {
        Guid id = Guid.NewGuid();
        await using NpgsqlConnection connection = new(api.AdminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(InsertTransactionSql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("account", AccountId);
        command.Parameters.AddWithValue("customer", CustomerId);
        command.Parameters.AddWithValue("reference", "T" + id.ToString("N")[..20]);
        command.Parameters.AddWithValue("amount", 250.50m);
        command.Parameters.AddWithValue("direction", direction.ToString());
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("posted", status == TransactionStatus.Pending ? DBNull.Value : date.AddHours(6));
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>A dispute of this customer on <paramref name="transactionId"/>, inserted directly (no eligibility, no API).</summary>
    /// <param name="createdAt">Set explicitly to give several disputes the same instant (keyset tie-breaks); now by default.</param>
    /// <param name="id">Set explicitly to control the order of ids (and so of the heap); a new one by default.</param>
    public async Task<Guid> AddDisputeAsync(ApiFactory api, Guid transactionId, DisputeStatus status, DateTimeOffset? createdAt = null, Guid? id = null)
    {
        Guid disputeId = id ?? Guid.NewGuid();
        await using NpgsqlConnection connection = new(api.AdminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(InsertDisputeSql, connection);
        command.Parameters.AddWithValue("id", disputeId);
        command.Parameters.AddWithValue("transaction", transactionId);
        command.Parameters.AddWithValue("customer", CustomerId);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("subject", User.Subject);
        command.Parameters.AddWithValue("created", createdAt ?? DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
        return disputeId;
    }

    /// <summary>A posted debit five days old: eligible for a dispute under the default policy.</summary>
    public Task<Guid> AddEligibleTransactionAsync(ApiFactory api) =>
        AddTransactionAsync(api, TransactionStatus.Posted, TransactionDirection.Debit, DateTimeOffset.UtcNow.AddDays(-5));
}
