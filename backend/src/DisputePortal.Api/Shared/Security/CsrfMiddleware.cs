using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Health;
using DisputePortal.Api.Shared.Observability;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// Requires <c>X-CSRF: 1</c> on every /api request and every non-GET /bff request. A cross-site page cannot add a
/// custom header without a CORS preflight, and the API has no CORS policy, so the preflight always fails.
/// Runs before authentication, so a forged request is refused before its cookie is even read.
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next, SecurityMetrics metrics, IOptions<ManagementSettings> ports)
{
    public const string HeaderName = "X-CSRF";
    public const string RequiredValue = "1";
    public const string Code = "csrf_required";

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresHeader(context.Request)
            && !IsManagementPortEndpoint(context, ports.Value.Management)
            && !string.Equals(context.Request.Headers[HeaderName], RequiredValue, StringComparison.Ordinal))
        {
            metrics.Record(SecurityMetrics.CsrfRejected);
            await ProblemResponses.WriteAsync(context, StatusCodes.Status403Forbidden, Code, "Send the X-CSRF: 1 header with this request.");
            return;
        }

        await next(context);
    }

    public static bool RequiresHeader(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || (request.Path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method));

    /// <summary>
    /// The one exemption: an endpoint marked for the management port AND reached on the management port, which is
    /// Keycloak's back-channel logout (a server-to-server POST, authenticated by its signed token). That port is never
    /// published and no browser can reach it, so there is no cross-site request to forge. On the public port every route,
    /// marked or not, still needs the header (ADR-011).
    /// </summary>
    public static bool IsManagementPortEndpoint(HttpContext context, int managementPort) =>
        context.Connection.LocalPort == managementPort && ManagementPortEndpoint.IsMarked(context);
}
