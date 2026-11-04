using DisputePortal.Api.Features.Customers;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// Demo data, run by <c>--migrate</c> through EF Core's UseAsyncSeeding; idempotent. The Keycloak subjects are fixed
/// and match deploy/keycloak's realm users. The agent has no customer row: agents are not customers.
/// </summary>
public static class Seeder
{
    public const string ThandiSubject = "a1b2c3d4-0000-4000-8000-000000000001";
    public const string SiphoSubject = "a1b2c3d4-0000-4000-8000-000000000002";
    public const string AgentSubject = "a1b2c3d4-0000-4000-8000-0000000000a1";

    public static readonly Guid ThandiCustomerId = Guid.Parse("c0570000-0000-4000-8000-000000000001");
    public static readonly Guid SiphoCustomerId = Guid.Parse("c0570000-0000-4000-8000-000000000002");

    public static IReadOnlyList<Customer> Customers { get; } =
    [
        new() { Id = ThandiCustomerId, KeycloakSubject = ThandiSubject, DisplayName = "Thandi Nkosi" },
        new() { Id = SiphoCustomerId, KeycloakSubject = SiphoSubject, DisplayName = "Sipho Dlamini" },
    ];

    // The flag says whether EF just created the store; seeding is idempotent, so it is not needed.
    public static async Task SeedAsync(DbContext context, bool _, CancellationToken cancellationToken)
    {
        DbSet<Customer> customers = context.Set<Customer>();
        List<Guid> existing = await customers.Select(customer => customer.Id).ToListAsync(cancellationToken);
        foreach (Customer customer in Customers.Where(customer => !existing.Contains(customer.Id)))
        {
            customers.Add(new Customer { Id = customer.Id, KeycloakSubject = customer.KeycloakSubject, DisplayName = customer.DisplayName });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
