using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Shared.Health;

/// <summary>
/// Holds startup until the readiness checks pass, with bounded backoff, so a missing stack fails fast with one clear
/// line. Runs the registered checks directly: the health check service would log a stack trace for every attempt.
/// </summary>
public static class DependencyGate
{
    public const string StartStackHint = "start the stack with docker compose up --build";

    private const int Attempts = 6;
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(500);

    public static async Task<bool> WaitAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        HealthCheckRegistration[] readiness = [.. services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Where(registration => registration.Tags.Contains(ManagementSettings.ReadyTag))];
        TimeSpan delay = FirstDelay;
        string[] unreachable = await UnreachableAsync(services, readiness, cancellationToken);
        for (int attempt = 1; attempt < Attempts && unreachable.Length > 0; attempt++)
        {
            logger.LogDebug("Waiting for {Dependencies}", string.Join(", ", unreachable));
            await Task.Delay(delay, cancellationToken);
            delay *= 2;
            unreachable = await UnreachableAsync(services, readiness, cancellationToken);
        }

        if (unreachable.Length == 0)
        {
            return true;
        }

        logger.LogCritical("Required dependency unreachable ({Dependencies}) after {Attempts} attempts; " + StartStackHint, string.Join(", ", unreachable), Attempts);
        return false;
    }

    private static async Task<string[]> UnreachableAsync(IServiceProvider services, HealthCheckRegistration[] readiness, CancellationToken cancellationToken)
    {
        List<string> unreachable = [];
        foreach (HealthCheckRegistration registration in readiness)
        {
            if (!await IsHealthyAsync(services, registration, cancellationToken))
            {
                unreachable.Add(registration.Name);
            }
        }

        return [.. unreachable];
    }

    private static async Task<bool> IsHealthyAsync(IServiceProvider services, HealthCheckRegistration registration, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            HealthCheckResult result = await registration.Factory(scope.ServiceProvider)
                .CheckHealthAsync(new HealthCheckContext { Registration = registration }, cancellationToken);
            return result.Status == HealthStatus.Healthy;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A probe: any failure (refused connection, bad credentials, missing setting) means not ready yet.
            return false;
        }
    }
}
