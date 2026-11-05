using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Primitives;

namespace DisputePortal.IntegrationTests;

/// <summary>TestServer has no socket peer; this sets one per request, before the app's forwarded-headers middleware.</summary>
public sealed class PeerAddressFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Peer";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out StringValues peer))
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}
