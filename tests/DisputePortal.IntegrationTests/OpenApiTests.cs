using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using DisputePortal.Api.Shared.OpenApi;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace DisputePortal.IntegrationTests;

/// <summary>(g) The runtime OpenAPI document matches the committed docs/openapi.json; UPDATE_SNAPSHOT=1 rewrites it.</summary>
[Collection("api")]
public sealed class OpenApiTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions SnapshotFormat = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task TheDocumentMatchesTheCommittedSnapshot()
    {
        string snapshotPath = RepositoryFiles.PathOf("docs", "openapi.json");
        string actual = await DocumentAsync();

        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOT") == "1")
        {
            await File.WriteAllTextAsync(snapshotPath, actual);
        }

        Assert.True(File.Exists(snapshotPath), "docs/openapi.json is missing; run the tests once with UPDATE_SNAPSHOT=1.");
        Assert.Equal(await File.ReadAllTextAsync(snapshotPath), actual);
    }

    [Fact]
    public async Task TheDocumentDescribesTheSessionCookieAndTheCsrfHeader()
    {
        using JsonDocument document = JsonDocument.Parse(await DocumentAsync());

        JsonElement schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal("cookie", schemes.GetProperty("session").GetProperty("in").GetString());
        Assert.Equal("dp_session", schemes.GetProperty("session").GetProperty("name").GetString());
        Assert.Equal("X-CSRF", schemes.GetProperty("csrf").GetProperty("name").GetString());
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/me", out _));
    }

    // Signed in, so the fallback policy (deny by default, unmatched paths included) cannot hide a served document behind a 401.
    [Fact]
    public async Task AProductionHostDoesNotServeTheDocument()
    {
        BrowserSession session = await BrowserSession.SignInAsync(api, new IdpUser(Seeder.ThandiSubject, "Thandi Nkosi", [Roles.Customer]));
        await using WebApplicationFactory<Program> production = api.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));
        using HttpClient client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        HttpResponseMessage response = await client.SendAsync(session.Request(HttpMethod.Get, ApiDocumentation.DocumentPath));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> DocumentAsync()
    {
        using HttpClient client = api.CreateClient();
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync(ApiDocumentation.DocumentPath));
        return JsonSerializer.Serialize(document.RootElement, SnapshotFormat) + "\n";
    }
}
