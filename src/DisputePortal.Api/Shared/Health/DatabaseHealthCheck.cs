using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace DisputePortal.Api.Shared.Health;

/// <summary>One round trip as the app role; a pooled open alone would not prove the server answers.</summary>
public sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    private const string PingSql = "SELECT 1";

    // A thrown exception is reported as Unhealthy (503) by the health check service.
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(PingSql);
        await command.ExecuteScalarAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
