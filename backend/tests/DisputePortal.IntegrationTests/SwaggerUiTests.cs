using System.Net;
using DisputePortal.Api.Shared.OpenApi;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The Swagger UI and the document it reads are served under /api only when OpenApi:Enabled is true (ADR-013). The shared
/// host turns it on; the hosts below derive from it with the flag removed, false, or true in another environment.
/// </summary>
[Collection("api")]
public sealed class SwaggerUiTests(ApiFactory api)
{
    private const string UiPage = ApiDocumentation.UiPath + "/index.html";
    private const string UiScript = ApiDocumentation.UiPath + "/index.js";

    [Theory]
    [InlineData(UiPage, "text/html")]
    [InlineData(ApiDocumentation.DocumentPath, "application/json")]
    public async Task WithOpenApiEnabledTheUiAndItsDocumentAnswerWithoutASession(string path, string mediaType)
    {
        using HttpClient client = api.CreateBrowserClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    // Microsoft.AspNetCore.OpenApi stays the only document source; the UI just points at it.
    // Swashbuckle writes the document URL into its embedded index.js (configObject), not into index.html.
    [Fact]
    public async Task TheUiReadsTheBuiltInDocumentUnderApi()
    {
        using HttpClient client = api.CreateBrowserClient();

        string script = await client.GetStringAsync(UiScript);

        Assert.Contains(ApiDocumentation.DocumentPath, script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task WithOpenApiEnabledTheOldRootPathsAreNotServed(string path)
    {
        HttpResponseMessage response = await SendSignedInAsync(api, path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Signed in, so the fallback policy (deny by default, unmatched paths included) cannot hide a served page behind a 401.
    [Theory]
    [InlineData(UiPage, "Development")]
    [InlineData(ApiDocumentation.DocumentPath, "Development")]
    [InlineData(UiPage, "Production")]
    [InlineData(ApiDocumentation.DocumentPath, "Production")]
    public async Task WithOpenApiDisabledNothingIsServedInAnyEnvironment(string path, string environment)
    {
        await using WebApplicationFactory<Program> disabled = api.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(ApiDocumentation.EnabledSetting, "false");
        });

        HttpResponseMessage response = await SendSignedInAsync(disabled, path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // The default is off: an empty setting (what an unset Compose variable or a blank override looks like) serves neither,
    // in every environment, and does not stop the host.
    [Theory]
    [InlineData(UiPage, "Development")]
    [InlineData(ApiDocumentation.DocumentPath, "Development")]
    [InlineData(UiPage, "Production")]
    [InlineData(ApiDocumentation.DocumentPath, "Production")]
    public async Task WithOpenApiUnsetNothingIsServedInAnyEnvironment(string path, string environment)
    {
        await using WebApplicationFactory<Program> unset = api.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(ApiDocumentation.EnabledSetting, "");
        });

        HttpResponseMessage response = await SendSignedInAsync(unset, path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // With nothing mapped, the documentation paths meet the CSRF rule and then the fallback policy before any 404: the answers
    // for every unknown /api path (403 without the header, 401 with it), so a disabled UI is indistinguishable from one that
    // never existed, and the CSRF exemption does not exist without the endpoint.
    [Theory]
    [InlineData(UiPage, false, HttpStatusCode.Forbidden)]
    [InlineData(ApiDocumentation.DocumentPath, false, HttpStatusCode.Forbidden)]
    [InlineData(UiPage, true, HttpStatusCode.Unauthorized)]
    [InlineData(ApiDocumentation.DocumentPath, true, HttpStatusCode.Unauthorized)]
    public async Task WithOpenApiDisabledAnAnonymousCallerGetsTheAnswerOfAnyUnknownApiPath(string path, bool csrfHeader, HttpStatusCode expected)
    {
        await using WebApplicationFactory<Program> disabled = api.WithWebHostBuilder(builder => builder.UseSetting(ApiDocumentation.EnabledSetting, "false"));
        using HttpClient client = disabled.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        if (csrfHeader)
        {
            client.DefaultRequestHeaders.Add(CsrfMiddleware.HeaderName, CsrfMiddleware.RequiredValue);
        }

        HttpResponseMessage response = await client.GetAsync(path);
        HttpResponseMessage unknown = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(expected, unknown.StatusCode);
        Assert.Equal(expected, response.StatusCode);
    }

    // The flag, not the environment, decides: Compose runs Production and turns it on with OPENAPI_ENABLED=true.
    [Theory]
    [InlineData(UiPage)]
    [InlineData(ApiDocumentation.DocumentPath)]
    public async Task AProductionHostServesTheUiAndTheDocumentAnonymouslyWhenOpenApiIsEnabled(string path)
    {
        await using WebApplicationFactory<Program> production = api.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting(ApiDocumentation.EnabledSetting, "true");
        });
        using HttpClient client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A session is created against the shared host; the cookie works on any derived host (same Redis, same keys).
    private async Task<HttpResponseMessage> SendSignedInAsync(WebApplicationFactory<Program> host, string path)
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, new IdpUser(Seeder.ThandiSubject, "Thandi Nkosi", [Roles.Customer]));
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        return await client.SendAsync(session.Request(HttpMethod.Get, path));
    }
}
