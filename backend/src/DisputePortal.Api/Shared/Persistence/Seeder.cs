using DisputePortal.Api.Features.Customers;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Domain.Eligibility;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// Demo data, run by <c>--migrate</c> (as the owner) through EF Core's UseAsyncSeeding; idempotent. The Keycloak
/// subjects are fixed and match deploy/keycloak's realm users; the agent has no customer row. Each customer's accounts
/// and transactions are seeded while acting as that customer, so the Owner filter is never bypassed, and only when the
/// customer has none yet. Re-seed with <c>docker compose down -v</c>.
/// </summary>
public static class Seeder
{
    public const string ThandiSubject = "a1b2c3d4-0000-4000-8000-000000000001";
    public const string SiphoSubject = "a1b2c3d4-0000-4000-8000-000000000002";
    public const string AgentSubject = "a1b2c3d4-0000-4000-8000-0000000000a1";

    public static readonly Guid ThandiCustomerId = Guid.Parse("c0570000-0000-4000-8000-000000000001");
    public static readonly Guid SiphoCustomerId = Guid.Parse("c0570000-0000-4000-8000-000000000002");

    private const string Rand = "ZAR";

    public static IReadOnlyList<Customer> Customers { get; } =
    [
        new() { Id = ThandiCustomerId, KeycloakSubject = ThandiSubject, DisplayName = "Thandi Nkosi" },
        new() { Id = SiphoCustomerId, KeycloakSubject = SiphoSubject, DisplayName = "Sipho Dlamini" },
    ];

    /// <summary>Thandi has a cheque and a credit-card account, Sipho one cheque account.</summary>
    public static IReadOnlyList<Account> Accounts { get; } =
    [
        new() { Id = Guid.Parse("acc00000-0000-4000-8000-000000000011"), CustomerId = ThandiCustomerId, MaskedNumber = "**** 4821", Currency = Rand },
        new() { Id = Guid.Parse("acc00000-0000-4000-8000-000000000012"), CustomerId = ThandiCustomerId, MaskedNumber = "**** 9907", Currency = Rand },
        new() { Id = Guid.Parse("acc00000-0000-4000-8000-000000000021"), CustomerId = SiphoCustomerId, MaskedNumber = "**** 3356", Currency = Rand },
    ];

    public static async Task SeedAsync(
        DbContext context, CurrentCustomer currentCustomer, TimeProvider time, DisputePolicyOptions policy, CancellationToken cancellationToken)
    {
        await SeedCustomersAsync(context, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        for (int ordinal = 0; ordinal < Customers.Count; ordinal++)
        {
            Guid customerId = Customers[ordinal].Id;
            currentCustomer.Set(customerId);
            if (await context.Set<Transaction>().AnyAsync(cancellationToken))
            {
                continue;
            }

            List<Account> accounts = [.. Accounts.Where(account => account.CustomerId == customerId)];
            List<Guid> existingAccounts = await context.Set<Account>().Select(account => account.Id).ToListAsync(cancellationToken);
            context.Set<Account>().AddRange(accounts.Where(account => !existingAccounts.Contains(account.Id)).Select(Copy));
            context.Set<Transaction>().AddRange(DemoTransactions.For(ordinal + 1, customerId, accounts, now, policy.WindowDays));
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedCustomersAsync(DbContext context, CancellationToken cancellationToken)
    {
        DbSet<Customer> customers = context.Set<Customer>();
        List<Guid> existing = await customers.Select(customer => customer.Id).ToListAsync(cancellationToken);
        customers.AddRange(Customers.Where(customer => !existing.Contains(customer.Id))
            .Select(customer => new Customer { Id = customer.Id, KeycloakSubject = customer.KeycloakSubject, DisplayName = customer.DisplayName }));
        await context.SaveChangesAsync(cancellationToken);
    }

    // The static list is shared; the change tracker needs its own instances.
    private static Account Copy(Account account) =>
        new() { Id = account.Id, CustomerId = account.CustomerId, MaskedNumber = account.MaskedNumber, Currency = account.Currency };
}
