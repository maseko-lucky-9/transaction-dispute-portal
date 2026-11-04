using System.Net;
using System.Net.Http.Json;
using DisputePortal.Api.Shared.Secrets;
using Microsoft.Extensions.Configuration;

namespace DisputePortal.UnitTests;

/// <summary>Vault's HTTP API answered in memory: AppRole login, then KV v2 reads mapped onto configuration keys.</summary>
public sealed class VaultConfigurationProviderTests : IDisposable
{
    private const string Address = "http://vault.test:8200";

    private readonly string directory = Directory.CreateTempSubdirectory("vault-test").FullName;
    private readonly List<string> requested = [];

    [Fact]
    public void TheApiPathsLandUnderTheirConfigurationPrefixes()
    {
        IConfiguration configuration = Build(migrate: false, AnswerVault);

        Assert.Equal("postgres", configuration["Database:Host"]);
        Assert.Equal("5432", configuration["Database:Port"]);
        Assert.Equal("dispute_app", configuration["Database:Username"]);
        Assert.Equal("redis-pass", configuration["Redis:Password"]);
        Assert.Equal("kc-secret", configuration["Keycloak:ClientSecret"]);
        Assert.Null(configuration["DatabaseOwner:Username"]);
        Assert.Contains($"POST {Address}/v1/auth/approle/login", requested);
    }

    [Fact]
    public void MigrateReadsOnlyTheOwnerPath()
    {
        IConfiguration configuration = Build(migrate: true, AnswerVault);

        Assert.Equal("dispute_owner", configuration["DatabaseOwner:Username"]);
        Assert.Null(configuration["Database:Username"]);
        Assert.DoesNotContain(requested, request => request.Contains("/dispute-portal/database", StringComparison.Ordinal) && !request.Contains("database-owner", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnavailableVaultFailsAfterTheBoundedRetry()
    {
        VaultUnavailableException failure = Assert.Throws<VaultUnavailableException>(() =>
            Build(migrate: false, request => Track(request, new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        Assert.Contains("after 3 attempts", failure.Message, StringComparison.Ordinal);
        Assert.Equal(3, requested.Count(request => request.EndsWith("/approle/login", StringComparison.Ordinal)));
    }

    // Connection refused while the Vault container is still starting (the bootstrap race) is retried.
    [Fact]
    public void ConnectionRefusedTwiceThenAnswerStillLoadsTheSecrets()
    {
        int failuresLeft = 2;
        IConfiguration configuration = Build(migrate: false, request =>
        {
            if (failuresLeft > 0)
            {
                failuresLeft--;
                Track(request, new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                throw new HttpRequestException("Connection refused");
            }

            return AnswerVault(request);
        });

        Assert.Equal("kc-secret", configuration["Keycloak:ClientSecret"]);
        Assert.Equal(3, requested.Count(request => request.EndsWith("/approle/login", StringComparison.Ordinal)));
    }

    // A defect (Vault answering something that is not its API) fails at once instead of burning the retry budget.
    [Fact]
    public void ANonRetryableErrorFailsAfterExactlyOneRequest()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Build(migrate: false, request =>
            Track(request, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not vault</html>") })));

        Assert.Single(requested);
    }

    [Fact]
    public void TheRetryReallyBacksOff()
    {
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<VaultUnavailableException>(() =>
            Build(migrate: false, request => Track(request, new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), TimeSpan.FromMilliseconds(50)));

        // Three attempts wait 50 ms, then 100 ms.
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(150), $"elapsed {elapsed.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public void MissingCredentialFilesAreRetriedThenReported()
    {
        VaultSettings settings = new() { Address = Address, RoleIdFile = Path.Combine(directory, "absent"), SecretIdFile = Path.Combine(directory, "absent"), Attempts = 2, FirstRetryDelay = TimeSpan.FromMilliseconds(1) };

        Assert.Throws<VaultUnavailableException>(() =>
            new ConfigurationBuilder().Add(new VaultConfigurationSource(settings, VaultSecretPath.For(false))).Build());
    }

    private IConfiguration Build(bool migrate, Func<HttpRequestMessage, HttpResponseMessage> respond, TimeSpan? firstRetryDelay = null)
    {
        File.WriteAllText(Path.Combine(directory, "role_id"), "role-123\n");
        File.WriteAllText(Path.Combine(directory, "secret_id"), "secret-456\n");
        VaultSettings settings = new()
        {
            Address = Address,
            RoleIdFile = Path.Combine(directory, "role_id"),
            SecretIdFile = Path.Combine(directory, "secret_id"),
            Attempts = 3,
            FirstRetryDelay = firstRetryDelay ?? TimeSpan.FromMilliseconds(1),
        };
        return new ConfigurationBuilder()
            .Add(new VaultConfigurationSource(settings, VaultSecretPath.For(migrate)) { HttpMessageHandler = new Responder(respond) })
            .Build();
    }

    private HttpResponseMessage AnswerVault(HttpRequestMessage request) => request.RequestUri!.AbsolutePath switch
    {
        "/v1/auth/approle/login" => Track(request, Json(new { auth = new { client_token = "token", lease_duration = 60, renewable = false, policies = Array.Empty<string>() } })),
        "/v1/kv/data/dispute-portal/database" => Track(request, Secret(new() { ["Host"] = "postgres", ["Port"] = "5432", ["Name"] = "dispute_portal", ["Username"] = "dispute_app", ["Password"] = "app-pass" })),
        "/v1/kv/data/dispute-portal/database-owner" => Track(request, Secret(new() { ["Host"] = "postgres", ["Username"] = "dispute_owner", ["Password"] = "owner-pass" })),
        "/v1/kv/data/dispute-portal/redis" => Track(request, Secret(new() { ["Host"] = "redis", ["Username"] = "dispute_app", ["Password"] = "redis-pass" })),
        "/v1/kv/data/dispute-portal/keycloak" => Track(request, Secret(new() { ["ClientSecret"] = "kc-secret" })),
        _ => Track(request, new HttpResponseMessage(HttpStatusCode.Forbidden)),
    };

    private HttpResponseMessage Track(HttpRequestMessage request, HttpResponseMessage response)
    {
        requested.Add(request.Method + " " + request.RequestUri!.GetLeftPart(UriPartial.Path));
        return response;
    }

    private static HttpResponseMessage Secret(Dictionary<string, string> data) =>
        Json(new { data = new { data, metadata = new { version = 1, created_time = "2026-11-04T00:00:00Z", destroyed = false } } });

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private sealed class Responder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
