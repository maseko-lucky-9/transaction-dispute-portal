using DisputePortal.Api.Features.Customers;
using DisputePortal.Api.Features.Transactions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// One context per request, never pooled: the Owner filter reads the request's <see cref="ICurrentCustomer"/>, and a
/// pooled context would carry it into another request. It also stores the DataProtection key ring for every replica.
/// </summary>
public sealed class DisputePortalDbContext(DbContextOptions<DisputePortalDbContext> options, ICurrentCustomer currentCustomer)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>The named filter every customer-owned entity carries; only Features/Agent may ignore it.</summary>
    public const string OwnerFilter = "Owner";

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Read by the filters at every query execution, so each request sees only its own customer.
    internal Guid? CurrentCustomerId => currentCustomer.CustomerId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DisputePortalDbContext).Assembly);
        // Fail closed: no current customer means no rows, never all rows.
        modelBuilder.Entity<Account>().HasQueryFilter(OwnerFilter, account => CurrentCustomerId != null && account.CustomerId == CurrentCustomerId);
        modelBuilder.Entity<Transaction>().HasQueryFilter(OwnerFilter, transaction => CurrentCustomerId != null && transaction.CustomerId == CurrentCustomerId);
    }
}
