using System.Text.Encodings.Web;
using System.Text.Json;
using DisputePortal.Api.Shared.OpenApi;

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

    // Swagger UI only sends X-CSRF for operations that reference the scheme; without it every /api "Try it out" is a 403.
    [Fact]
    public async Task EveryApiOperationRequiresTheCsrfHeaderAndNoBffOperationDoes()
    {
        using JsonDocument document = JsonDocument.Parse(await DocumentAsync());
        JsonElement csrf = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("csrf");
        Assert.Equal("apiKey", csrf.GetProperty("type").GetString());
        Assert.Equal("header", csrf.GetProperty("in").GetString());

        int apiOperations = 0;
        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                string name = path.Name + " " + operation.Name;
                bool requiresCsrf = operation.Value.TryGetProperty("security", out JsonElement security)
                    && security.EnumerateArray().Any(requirement => requirement.TryGetProperty("csrf", out _));
                if (path.Name.StartsWith("/api/", StringComparison.Ordinal))
                {
                    apiOperations++;
                    Assert.True(requiresCsrf, name + " lacks the csrf security requirement");
                }
                else
                {
                    Assert.False(requiresCsrf, name + " must not carry the csrf requirement");
                }
            }
        }

        Assert.True(apiOperations > 0);
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
        Assert.Equal(["UnderReview", "Accepted", "Rejected", "Submitted"], document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("TransitionRequest").GetProperty("properties").GetProperty("toStatus").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    // ADR-019: take next is 200 with the dispute and its ETag, or 204 when there is nothing to take; a move by an agent
    // who is not reviewing the dispute is a 409 (not_assignee); the queue documents its assignment filter.
    [Fact]
    public async Task TakeNextDocuments200WithAnETagAnd204AndTheQueueItsAssignmentFilter()
    {
        using JsonDocument document = JsonDocument.Parse(await DocumentAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");
        JsonElement takeNext = paths.GetProperty("/api/v1/agent/disputes/take-next").GetProperty("post");
        JsonElement queue = paths.GetProperty("/api/v1/agent/disputes").GetProperty("get");

        Assert.Equal("TakeNextDispute", takeNext.GetProperty("operationId").GetString());
        JsonElement responses = takeNext.GetProperty("responses");
        foreach (string status in new[] { "200", "204", "401", "403" })
        {
            Assert.True(responses.TryGetProperty(status, out _), "take-next " + status);
        }

        Assert.True(responses.GetProperty("200").GetProperty("headers").TryGetProperty("ETag", out _));
        Assert.Equal("#/components/schemas/AgentDisputeDetail",
            responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.False(responses.GetProperty("204").TryGetProperty("content", out _));
        Assert.False(takeNext.TryGetProperty("requestBody", out _));
        Assert.True(paths.GetProperty("/api/v1/agent/disputes/{id}/transitions").GetProperty("post").GetProperty("responses").TryGetProperty("409", out _));
        JsonElement assignment = queue.GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "assignment");
        Assert.Equal(["unclaimed", "mine", "others"], assignment.GetProperty("schema").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    private async Task<string> DocumentAsync()
    {
        using HttpClient client = api.CreateClient();
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync(ApiDocumentation.DocumentPath));
        return JsonSerializer.Serialize(document.RootElement, SnapshotFormat) + "\n";
    }
}
