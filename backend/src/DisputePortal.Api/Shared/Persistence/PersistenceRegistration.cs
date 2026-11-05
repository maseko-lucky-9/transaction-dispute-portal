using DisputePortal.Api.Features.Customers;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Domain.Eligibility;
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

    /// <param name="use">The API binds <see cref="DatabaseSettings.AppSection"/>; --migrate binds <see cref="DatabaseSettings.OwnerSection"/>.</param>
    public static IServiceCollection AddPersistence(this IServiceCollection services, DatabaseUse use)
    {
        string section = use == DatabaseUse.Migrations ? DatabaseSettings.OwnerSection : DatabaseSettings.AppSection;
        services.AddOptions<DatabaseSettings>().BindConfiguration(section).ValidateDataAnnotations().ValidateOnStart();
        // Resolved on first use, so test settings apply.
        services.AddSingleton(provider => NpgsqlDataSource.Create(provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.ToConnectionString(use)));
        services.AddScoped<CurrentCustomer>();
        services.AddScoped<ICurrentCustomer>(provider => provider.GetRequiredService<CurrentCustomer>());
        // Not pooled: the Owner filter reads per-request state. The seeder acts as each customer in turn through the
        // same request-scoped CurrentCustomer, so it never needs to bypass the filter.
        services.AddDbContext<DisputePortalDbContext>((provider, options) => Configure(options, provider.GetRequiredService<NpgsqlDataSource>())
            .UseAsyncSeeding((context, _, cancellationToken) => Seeder.SeedAsync(
                context,
                provider.GetRequiredService<CurrentCustomer>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IOptions<DisputePolicyOptions>>().Value,
                cancellationToken)));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ICustomerLock, AdvisoryCustomerLock>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
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
            .UseSnakeCaseNamingConvention();
}
