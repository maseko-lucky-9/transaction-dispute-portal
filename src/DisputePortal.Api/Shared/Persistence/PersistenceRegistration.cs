using DisputePortal.Api.Features.Customers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DisputePortal.Api.Shared.Persistence;

public static class PersistenceRegistration
{
    public const string DataProtectionApplicationName = "dispute-portal";

    private const int MaxRetryCount = 3;
    private const int CommandTimeoutSeconds = 10;

    /// <param name="section"><see cref="DatabaseSettings.AppSection"/> for the API, <see cref="DatabaseSettings.OwnerSection"/> for --migrate.</param>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string section)
    {
        services.AddOptions<DatabaseSettings>().BindConfiguration(section).ValidateDataAnnotations().ValidateOnStart();
        // Resolved on first use, so test settings apply.
        services.AddSingleton(provider => NpgsqlDataSource.Create(provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.ToConnectionString()));
        // Not pooled: a later slice adds a per-request owner filter, and pooled contexts would carry it across requests.
        services.AddDbContext<DisputePortalDbContext>((provider, options) => Configure(options, provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        return services;
    }

    /// <summary>One shared key ring in Postgres under a fixed name, so every replica reads every cookie and ticket.</summary>
    /// <remarks>
    /// ponytail: keys sit unencrypted at rest (no XML encryptor), guarded by the database grants; the upgrade is
    /// ProtectKeysWithCertificate with a certificate delivered by Vault.
    /// </remarks>
    public static IServiceCollection AddDataProtectionKeyRing(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<DisputePortalDbContext>();
        return services;
    }

    // Shared with the design-time factory, so migrations see the same naming convention as the running app.
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.EnableRetryOnFailure(MaxRetryCount).CommandTimeout(CommandTimeoutSeconds))
            .UseSnakeCaseNamingConvention()
            .UseAsyncSeeding(Seeder.SeedAsync);
}
