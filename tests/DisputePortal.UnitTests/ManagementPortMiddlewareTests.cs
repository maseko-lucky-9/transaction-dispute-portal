using DisputePortal.Api.Shared.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DisputePortal.UnitTests;

/// <summary>The split uses the socket's local port, which a client cannot forge, never the Host header, which it can.</summary>
public sealed class ManagementPortMiddlewareTests
{
    [Theory]
    [InlineData(8081, "/health/ready", true)]
    [InlineData(8081, "/health/live", true)]
    [InlineData(8081, "/api/v1/me", false)]
    [InlineData(8081, "/bff/login", false)]
    [InlineData(8080, "/health/ready", false)]
    [InlineData(8080, "/api/v1/me", true)]
    public async Task EachPortServesOnlyItsOwnPaths(int localPort, string path, bool served)
    {
        (bool reachedNext, int status) = await InvokeAsync(localPort, "localhost", path);

        Assert.Equal(served, reachedNext);
        Assert.Equal(served ? StatusCodes.Status200OK : StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task AHostHeaderNamingTheManagementPortDoesNotReachHealthOnThePublicPort()
    {
        (bool reachedNext, int status) = await InvokeAsync(8080, "localhost:8081", "/health/ready");

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    private static async Task<(bool ReachedNext, int Status)> InvokeAsync(int localPort, string host, string path)
    {
        bool reachedNext = false;
        ManagementPortMiddleware middleware = new(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        }, Options.Create(new ManagementSettings()));
        DefaultHttpContext context = new();
        context.Connection.LocalPort = localPort;
        context.Request.Host = new HostString(host);
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        return (reachedNext, context.Response.StatusCode);
    }
}
