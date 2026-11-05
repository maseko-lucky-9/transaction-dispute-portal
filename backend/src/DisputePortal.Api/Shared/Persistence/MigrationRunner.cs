using DisputePortal.Api.Shared.Health;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// <c>--migrate</c>: as the schema owner, apply migrations (which also seed and grant), then make sure a DataProtection
/// key exists, so API replicas starting together never race to create the first one.
/// </summary>
public static class MigrationRunner
{
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromDays(90);

    // The API's 10 s command timeout would cut off a long index build; migrations get far longer.
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrationRunner));
        try
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            DisputePortalDbContext db = scope.ServiceProvider.GetRequiredService<DisputePortalDbContext>();
            db.Database.SetCommandTimeout(CommandTimeout);
            await db.Database.MigrateAsync(cancellationToken);
            EnsureDataProtectionKey(scope.ServiceProvider.GetRequiredService<IKeyManager>(), services.GetRequiredService<TimeProvider>().GetUtcNow());
            logger.LogInformation("Migrations applied; seed data and data-protection key in place");
            return 0;
        }
        catch (Exception exception)
        {
            // The process boundary: one line for the operator, with the exception type but no connection details.
            logger.LogCritical("Migration failed ({Error}); " + DependencyGate.StartStackHint, exception.GetType().Name + ": " + exception.Message);
            return 1;
        }
    }

    public static void EnsureDataProtectionKey(IKeyManager keyManager, DateTimeOffset now)
    {
        bool hasUsableKey = keyManager.GetAllKeys().Any(key => !key.IsRevoked && key.ActivationDate <= now && key.ExpirationDate > now);
        if (!hasUsableKey)
        {
            keyManager.CreateNewKey(now, now + KeyLifetime);
        }
    }
}
