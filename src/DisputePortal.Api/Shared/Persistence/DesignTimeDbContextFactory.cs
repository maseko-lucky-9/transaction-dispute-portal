using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>For <c>dotnet ef migrations add</c> only: builds the model without Vault or a reachable server.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DisputePortalDbContext>
{
    public DisputePortalDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<DisputePortalDbContext> options = new();
        PersistenceRegistration.Configure(options, NpgsqlDataSource.Create("Host=localhost;Database=dispute_portal"));
        return new DisputePortalDbContext(options.Options);
    }
}
