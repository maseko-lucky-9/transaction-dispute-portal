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

    // The agent routes' contract: If-Match required on the move, 412 and 428 documented, ETag on the 200s and the 412.
    [Fact]
    public async Task TheAgentRoutesDocumentIfMatchTheETagAnd412And428()
    {
        using JsonDocument document = JsonDocument.Parse(await DocumentAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");
        JsonElement move = paths.GetProperty("/api/v1/agent/disputes/{id}/transitions").GetProperty("post");
        JsonElement detail = paths.GetProperty("/api/v1/agent/disputes/{id}").GetProperty("get");
        JsonElement queue = paths.GetProperty("/api/v1/agent/disputes").GetProperty("get");

        JsonElement ifMatch = move.GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "If-Match");
        Assert.True(ifMatch.GetProperty("required").GetBoolean());
        Assert.Equal("header", ifMatch.GetProperty("in").GetString());
        JsonElement moveResponses = move.GetProperty("responses");
        foreach (string status in new[] { "200", "400", "401", "403", "404", "409", "412", "428" })
        {
            Assert.True(moveResponses.TryGetProperty(status, out _), "transition " + status);
        }

        Assert.True(moveResponses.GetProperty("200").GetProperty("headers").TryGetProperty("ETag", out _));
        Assert.True(moveResponses.GetProperty("412").GetProperty("headers").TryGetProperty("ETag", out _));
        Assert.True(detail.GetProperty("responses").GetProperty("200").GetProperty("headers").TryGetProperty("ETag", out _));
        Assert.True(detail.GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(queue.GetProperty("responses").TryGetProperty("400", out _));
        Assert.Equal(["UnderReview", "Accepted", "Rejected"], document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("TransitionRequest").GetProperty("properties").GetProperty("toStatus").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
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
