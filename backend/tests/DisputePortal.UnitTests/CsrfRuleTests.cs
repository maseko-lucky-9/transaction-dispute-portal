using DisputePortal.Api.Shared.Health;
using DisputePortal.Api.Shared.OpenApi;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Http;

namespace DisputePortal.UnitTests;

public sealed class CsrfRuleTests
{
    private const int ManagementPort = 8081;

    [Theory]
    [InlineData("GET", "/api/v1/me", true)]
    [InlineData("POST", "/api/v1/disputes", true)]
    [InlineData("GET", "/API/v1/me", true)]
    [InlineData("POST", "/bff/user", true)]
    [InlineData("DELETE", "/bff/session", true)]
    [InlineData("POST", "/bff/backchannel-logout", true)]
    [InlineData("GET", "/bff/user", false)]
    [InlineData("GET", "/bff/login", false)]
    [InlineData("HEAD", "/bff/user", false)]
    [InlineData("GET", "/signin-oidc", false)]
    [InlineData("POST", "/apiary", false)]
    public void TheHeaderIsRequiredOnApiAndOnNonGetBff(string method, string path, bool required)
    {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = path;

        Assert.Equal(required, CsrfMiddleware.RequiresHeader(context.Request));
    }

    // The exemption needs both: the management-port marker on the endpoint AND the management port itself. A marked
    // endpoint on the public port, or an unmarked one on the management port, still needs the header.
    [Theory]
    [InlineData(ManagementPort, true, true)]
    [InlineData(8080, true, false)]
    [InlineData(ManagementPort, false, false)]
    [InlineData(8080, false, false)]
    public void OnlyAManagementPortEndpointOnTheManagementPortIsExempt(int localPort, bool marked, bool exempt)
    {
        DefaultHttpContext context = new();
        context.Connection.LocalPort = localPort;
        context.Request.Method = "POST";
        context.Request.Path = "/bff/backchannel-logout";
        if (marked)
        {
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(ManagementPortEndpoint.Instance), "backchannel"));
        }

        Assert.Equal(exempt, CsrfMiddleware.IsManagementPortEndpoint(context, ManagementPort));
    }

    // A browser cannot send the header on a page load, so the Swagger UI and the document are exempt for reads, and only
    // when the routed endpoint carries the documentation marker: an unmarked /api/swagger path (UI switched off) is not.
    [Theory]
    [InlineData("GET", true, true)]
    [InlineData("HEAD", true, true)]
    [InlineData("POST", true, false)]
    [InlineData("DELETE", true, false)]
    [InlineData("GET", false, false)]
    public void OnlyAReadOfAMarkedDocumentationEndpointIsExempt(string method, bool marked, bool exempt)
    {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = "/api/swagger/index.html";
        if (marked)
        {
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(ApiDocumentationEndpoint.Instance), "swagger"));
        }

        Assert.Equal(exempt, CsrfMiddleware.IsApiDocumentationRead(context));
    }
}
