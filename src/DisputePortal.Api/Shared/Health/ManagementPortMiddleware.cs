using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Shared.Health;

/// <summary>
/// Splits the two listeners by the socket's local port, not the Host header (which a client controls):
/// the management port answers only the health paths, and the public port never answers them.
/// </summary>
public sealed class ManagementPortMiddleware(RequestDelegate next, IOptions<ManagementSettings> settings)
{
    public Task InvokeAsync(HttpContext context)
    {
        bool onManagementPort = context.Connection.LocalPort == settings.Value.Management;
        bool isHealthPath = context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
        if (onManagementPort != isHealthPath)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
