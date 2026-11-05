using System.Net;
using DisputePortal.Api.Shared.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DisputePortal.UnitTests;

/// <summary>The split uses the socket's local port, which a client cannot forge, never the Host header, which it can.</summary>
public sealed class ManagementPortMiddlewareTests
{
    private const string BackchannelPath = "/bff/backchannel-logout";
    private const string DataNetwork = "10.231.28.0/24";

    [Theory]
    [InlineData(8081, "/health/ready", true)]
    [InlineData(8081, "/health/live", true)]
    [InlineData(8081, "/api/v1/me", false)]
    [InlineData(8081, "/bff/login", false)]
    [InlineData(8080, "/health/ready", false)]
    [InlineData(8080, "/api/v1/me", true)]
    public async Task EachPortServesOnlyItsOwnPaths(int localPort, string path, bool served)
    {
        (bool reachedNext, int status) = await InvokeAsync(new ManagementSettings(), Request(localPort, "localhost", path));

        Assert.Equal(served, reachedNext);
        Assert.Equal(served ? StatusCodes.Status200OK : StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task AHostHeaderNamingTheManagementPortDoesNotReachHealthOnThePublicPort()
    {
        (bool reachedNext, int status) = await InvokeAsync(new ManagementSettings(), Request(8080, "localhost:8081", "/health/ready"));

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    // The marked endpoint, not the path: any spelling that routes to it (case, trailing slash) is refused on the public port.
    [Theory]
    [InlineData(BackchannelPath)]
    [InlineData("/BFF/Backchannel-Logout/")]
    public async Task AManagementOnlyEndpointIsServedOnTheManagementPortAndNeverOnThePublicOne(string path)
    {
        (bool onManagement, int managementStatus) = await InvokeAsync(new ManagementSettings(), Request(8081, "localhost", path, marked: true));
        (bool onPublic, int publicStatus) = await InvokeAsync(new ManagementSettings(), Request(8080, "localhost", path, marked: true));

        Assert.True(onManagement);
        Assert.Equal(StatusCodes.Status200OK, managementStatus);
        Assert.False(onPublic);
        Assert.Equal(StatusCodes.Status404NotFound, publicStatus);
    }

    [Fact]
    public async Task AnUnmarkedEndpointUnderTheSamePathPrefixIsNotServedOnTheManagementPort()
    {
        (bool reachedNext, int status) = await InvokeAsync(new ManagementSettings(), Request(8081, "localhost", "/bff/user", marked: false));

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    // The internal name passes host filtering only for Keycloak's call to the management port.
    [Theory]
    [InlineData("api-mgmt")]
    [InlineData("API-MGMT:8080")]
    public async Task TheInternalHostIsRefusedOnThePublicPort(string host)
    {
        (bool reachedNext, int status) = await InvokeAsync(Internal(), Request(8080, host, "/api/v1/me"));

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task TheInternalHostReachesTheBackchannelEndpointOnTheManagementPort()
    {
        (bool reachedNext, _) = await InvokeAsync(Internal(), Request(8081, "api-mgmt:8081", BackchannelPath, marked: true, peer: "10.231.28.7"));

        Assert.True(reachedNext);
    }

    [Fact]
    public async Task ThePublicHostStillWorksOnThePublicPortWhenAnInternalHostIsSet()
    {
        (bool reachedNext, _) = await InvokeAsync(Internal(), Request(8080, "localhost", "/api/v1/me", peer: "10.231.29.4"));

        Assert.True(reachedNext);
    }

    // With the data network configured, nginx (app network only) cannot reach the management port; the container's own
    // probe (loopback) still can. Kestrel reports IPv4 peers as IPv4-mapped IPv6 on its dual-stack listener.
    [Theory]
    [InlineData("10.231.28.7", true)]
    [InlineData("::ffff:10.231.28.7", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.231.29.4", false)]
    [InlineData("::ffff:10.231.29.4", false)]
    [InlineData("203.0.113.9", false)]
    [InlineData(null, false)]
    public async Task TheManagementPortAnswersOnlyLoopbackAndTheInternalNetwork(string? peer, bool admitted)
    {
        (bool backchannel, int status) = await InvokeAsync(Internal(), Request(8081, "api-mgmt:8081", BackchannelPath, marked: true, peer: peer));
        (bool health, _) = await InvokeAsync(Internal(), Request(8081, "localhost:8081", "/health/live", peer: peer));

        Assert.Equal(admitted, backchannel);
        Assert.Equal(admitted, health);
        Assert.Equal(admitted ? StatusCodes.Status200OK : StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task WithoutAnInternalNetworkThePeerIsNotChecked()
    {
        (bool reachedNext, _) = await InvokeAsync(new ManagementSettings(), Request(8081, "localhost", "/health/live", peer: "203.0.113.9"));

        Assert.True(reachedNext);
    }

    [Fact]
    public void AMalformedInternalNetworkStopsStartUp() =>
        Assert.Throws<FormatException>(() => new ManagementPortMiddleware(_ => Task.CompletedTask, Options.Create(new ManagementSettings { InternalNetwork = "10.231.28.0/33" })));

    private static ManagementSettings Internal() => new() { InternalHost = "api-mgmt", InternalNetwork = DataNetwork };

    private static DefaultHttpContext Request(int localPort, string host, string path, bool marked = false, string? peer = null)
    {
        DefaultHttpContext context = new();
        context.Connection.LocalPort = localPort;
        context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        if (marked)
        {
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(ManagementPortEndpoint.Instance), "backchannel"));
        }

        return context;
    }

    private static async Task<(bool ReachedNext, int Status)> InvokeAsync(ManagementSettings settings, DefaultHttpContext context)
    {
        bool reachedNext = false;
        ManagementPortMiddleware middleware = new(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        }, Options.Create(settings));

        await middleware.InvokeAsync(context);

        return (reachedNext, context.Response.StatusCode);
    }
}
