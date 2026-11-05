using System.Net;
using DisputePortal.Api.Shared.Health;

namespace DisputePortal.IntegrationTests;

/// <summary>(f) Health lives only on the management port, and the management port serves nothing else.</summary>
[Collection("api")]
public sealed class ManagementPortTests(ApiFactory api)
{
    private const string ManagementBaseUrl = "http://localhost:8081";
    private const string PublicBaseUrl = "http://localhost:8080";

    [Theory]
    [InlineData(ManagementSettings.ReadyPath)]
    [InlineData(ManagementSettings.LivePath)]
    public async Task HealthAnswersOnTheManagementPort(string path)
    {
        using HttpClient management = api.CreateBrowserClient(ManagementBaseUrl);

        HttpResponseMessage response = await management.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ManagementSettings.ReadyPath)]
    [InlineData(ManagementSettings.LivePath)]
    public async Task HealthIsNotReachableOnThePublicPort(string path)
    {
        using HttpClient publicPort = api.CreateBrowserClient(PublicBaseUrl);

        HttpResponseMessage response = await publicPort.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheManagementPortDoesNotServeTheApi()
    {
        using HttpClient management = api.CreateBrowserClient(ManagementBaseUrl);
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("X-CSRF", "1");

        HttpResponseMessage response = await management.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
