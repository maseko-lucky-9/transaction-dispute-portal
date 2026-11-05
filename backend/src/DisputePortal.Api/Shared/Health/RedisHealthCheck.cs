using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace DisputePortal.Api.Shared.Health;

/// <summary>A PING as the app's ACL user: the session store must answer before the replica takes traffic.</summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await redis.GetDatabase().PingAsync();
        return HealthCheckResult.Healthy();
    }
}
