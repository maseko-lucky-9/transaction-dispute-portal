using System.Data.Common;
using System.Text.Json;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// ADR-006's evidence: on 50,000 rows for one customer (plus other customers' rows as noise), the API's exact keyset
/// query, Owner filter included, is an index scan on (customer_id, transaction_date desc, id desc) with no Sort.
/// </summary>
[Collection("api")]
public sealed class TransactionPlanTests(ApiFactory api)
{
    private const int Rows = 50_000;
    private const int NoiseCustomers = 4;
    private const int NoiseRowsEach = 5_000;

    private const string GenerateSql = """
        INSERT INTO transactions (id, account_id, customer_id, reference, merchant_name, category, amount, currency, direction, status, transaction_date, posted_at)
        SELECT gen_random_uuid(), @account, @customer, 'P' || left(replace(@customer::text, '-', ''), 20) || g, 'Plan merchant', 'Test',
               10 + (g % 1000), 'ZAR', 'Debit', 'Posted', now() - make_interval(mins => g), now() - make_interval(mins => g)
        FROM generate_series(1, @rows) AS g
        """;

    [Fact]
    public async Task TheKeysetQueryIsAnIndexScanWithNoSortOn50kRows()
    {
        TestCustomer customer = await TestCustomer.CreateAsync(api);
        await GenerateAsync(customer, Rows);
        for (int noise = 0; noise < NoiseCustomers; noise++)
        {
            await GenerateAsync(await TestCustomer.CreateAsync(api), NoiseRowsEach);
        }

        await ExecuteAdminAsync("ANALYZE transactions; ANALYZE accounts;");

        List<JsonElement> firstPage = await PlanAsync(customer.CustomerId, new TransactionFilter(null, null, null), after: null);
        List<JsonElement> deepPage = await PlanAsync(customer.CustomerId, new TransactionFilter(null, null, null), new TransactionKey(DateTimeOffset.UtcNow.AddMinutes(-40_000), Guid.Empty));

        foreach (List<JsonElement> plan in new[] { firstPage, deepPage })
        {
            Assert.Contains(plan, node => node.GetProperty("Node Type").GetString() is "Index Scan" or "Index Only Scan"
                && node.TryGetProperty("Index Name", out JsonElement index) && index.GetString() == TransactionConfiguration.KeysetIndexName);
            Assert.DoesNotContain(plan, node => node.GetProperty("Node Type").GetString()!.Contains("Sort", StringComparison.Ordinal));
        }
    }

    // EXPLAIN of the command EF actually sends, parameters and Owner filter included.
    private async Task<List<JsonElement>> PlanAsync(Guid customerId, TransactionFilter filter, TransactionKey? after)
    {
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentCustomer>().Set(customerId);
        DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();
        TransactionRepository repository = new(db);
        await using DbCommand command = repository.PageQuery(filter, after, TransactionQueries.DefaultLimit + 1).CreateDbCommand();
        command.CommandText = "EXPLAIN (FORMAT JSON) " + command.CommandText;
        await db.Database.OpenConnectionAsync();
        using JsonDocument plan = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        return [.. Flatten(plan.RootElement[0].GetProperty("Plan").Clone())];
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node) =>
        new[] { node }.Concat(node.TryGetProperty("Plans", out JsonElement children) ? children.EnumerateArray().SelectMany(Flatten) : []);

    private async Task GenerateAsync(TestCustomer customer, int rows)
    {
        await using NpgsqlConnection connection = new(api.AdminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(GenerateSql, connection);
        command.Parameters.AddWithValue("account", customer.AccountId);
        command.Parameters.AddWithValue("customer", customer.CustomerId);
        command.Parameters.AddWithValue("rows", rows);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using NpgsqlConnection connection = new(api.AdminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
