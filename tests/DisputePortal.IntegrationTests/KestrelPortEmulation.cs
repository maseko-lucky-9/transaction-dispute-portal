using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// TestServer has no sockets, so it reports no local port. This sets it from the request URL, as Kestrel would from the
/// listener that accepted the connection, so the management-port split runs exactly as in production.
/// </summary>
public sealed class KestrelPortEmulation : IStartupFilter
{
    private const int DefaultHttpPort = 80;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.LocalPort = context.Request.Host.Port ?? DefaultHttpPort;
            return nextMiddleware(context);
        });
        next(app);
    };
}
