namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// <c>Cache-Control: no-store</c> on every /api and /bff response, errors included: personal data and session state
/// must never sit in a browser or proxy cache. Set as the response starts, after any handler has touched the headers.
/// </summary>
public sealed class NoStoreMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(static state =>
            {
                ((HttpResponse)state).Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            }, context.Response);
        }

        return next(context);
    }
}
