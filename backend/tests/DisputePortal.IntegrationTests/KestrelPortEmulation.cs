using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// TestServer has no sockets, so it reports no local port and no peer. This sets the port from the request URL, as Kestrel
/// would from the listener that accepted the connection, so the management-port split runs exactly as in production. A
/// management-port request with no peer gets Keycloak's data-network address (as in compose); <see cref="PeerAddressFilter"/>,
/// registered later, overrides it per request.
/// </summary>
public sealed class KestrelPortEmulation : IStartupFilter
{
    private const int DefaultHttpPort = 80;
    private static readonly IPAddress KeycloakPeer = IPAddress.Parse(ApiFactory.KeycloakPeer);

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.LocalPort = context.Request.Host.Port ?? DefaultHttpPort;
            if (context.Connection.LocalPort == ApiFactory.ManagementPort)
            {
                context.Connection.RemoteIpAddress ??= KeycloakPeer;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}
