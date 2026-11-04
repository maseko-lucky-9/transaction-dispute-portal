using System.Net;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Shared.Health;

/// <summary>
/// Splits the two listeners by the socket's local port, not the Host header (which a client controls): the management
/// port answers only the health paths and endpoints marked <see cref="ManagementPortEndpoint"/>, and the public port never
/// answers them. With <see cref="ManagementSettings.InternalNetwork"/> set, the management port also refuses peers outside
/// loopback and that network; the internal host name is refused on the public port (ADR-011).
/// </summary>
public sealed class ManagementPortMiddleware
{
    private readonly RequestDelegate next;
    private readonly ManagementSettings ports;
    private readonly IPNetwork? internalNetwork;

    public ManagementPortMiddleware(RequestDelegate next, IOptions<ManagementSettings> settings)
    {
        this.next = next;
        ports = settings.Value;
        // Parsed once, at start-up: a malformed CIDR stops the app instead of failing every request.
        internalNetwork = string.IsNullOrWhiteSpace(ports.InternalNetwork) ? null : IPNetwork.Parse(ports.InternalNetwork);
    }

    public Task InvokeAsync(HttpContext context)
    {
        bool onManagementPort = context.Connection.LocalPort == ports.Management;
        if (onManagementPort != IsManagementRequest(context) || (onManagementPort && !AdmitsPeer(context.Connection.RemoteIpAddress)))
        {
            return Refuse(context, StatusCodes.Status404NotFound);
        }

        // Host filtering let the internal name through only for Keycloak's call to the management port; anywhere else it
        // is a foreign host, answered like the framework answers one.
        if (!onManagementPort && IsInternalHost(context.Request.Host))
        {
            return Refuse(context, StatusCodes.Status400BadRequest);
        }

        return next(context);
    }

    public static bool IsManagementRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) || ManagementPortEndpoint.IsMarked(context);

    private bool IsInternalHost(HostString host) =>
        !string.IsNullOrWhiteSpace(ports.InternalHost) && string.Equals(host.Host, ports.InternalHost, StringComparison.OrdinalIgnoreCase);

    // Kestrel's dual-stack listener reports an IPv4 peer as ::ffff:a.b.c.d; the network is IPv4.
    private bool AdmitsPeer(IPAddress? peer)
    {
        if (internalNetwork is not { } network)
        {
            return true;
        }

        if (peer is null)
        {
            return false;
        }

        IPAddress address = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
        return IPAddress.IsLoopback(address) || network.Contains(address);
    }

    private static Task Refuse(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
