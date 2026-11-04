using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisputePortal.Api.Shared.Health;

public static class HealthEndpoints
{
    public static IServiceCollection AddReadinessChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres", tags: [ManagementSettings.ReadyTag])
            .AddCheck<RedisHealthCheck>("redis", tags: [ManagementSettings.ReadyTag]);
        return services;
    }

    /// <summary>Live answers while the process runs; ready also needs Postgres and Redis. Both only on the management port.</summary>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(ManagementSettings.LivePath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous().DisableRateLimiting().ExcludeFromDescription().ServeOnManagementPortOnly();
        app.MapHealthChecks(ManagementSettings.ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(ManagementSettings.ReadyTag) })
            .AllowAnonymous().DisableRateLimiting().ExcludeFromDescription().ServeOnManagementPortOnly();
        return app;
    }
}
