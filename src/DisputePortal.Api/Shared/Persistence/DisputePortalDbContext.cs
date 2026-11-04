using DisputePortal.Api.Features.Customers;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// One context per request, never pooled: later slices put a per-request owner filter on it (plan: "Authorization").
/// It also stores the ASP.NET Core DataProtection key ring, so every replica shares one ring.
/// </summary>
public sealed class DisputePortalDbContext(DbContextOptions<DisputePortalDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DisputePortalDbContext).Assembly);
}
