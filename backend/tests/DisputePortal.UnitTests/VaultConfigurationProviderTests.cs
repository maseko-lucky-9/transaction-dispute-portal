using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Api.Shared.Secrets;
using Microsoft.Extensions.Configuration;

namespace DisputePortal.UnitTests;

/// <summary>Vault's HTTP API answered in memory: AppRole or Kubernetes login, then KV v2 reads mapped onto configuration keys.</summary>
public sealed class VaultConfigurationProviderTests : IDisposable
{
    private const string Address = "http://vault.test:8200";

    // A distinctive stand-in for the service-account token, so a leak into a message is easy to spot.
    private const string Jwt = "test-service-account-token-7f3c9a";

    private readonly string directory = Directory.CreateTempSubdirectory("vault-test").FullName;
    private readonly List<string> requested = [];
    private readonly List<JsonElement> logins = [];

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

    [Fact]
    public void KubernetesLoginPostsTheRoleAndTheTrimmedTokenToTheDefaultMount()
    {
        File.WriteAllText(TokenFile, Jwt + "\n");

        IConfiguration configuration = Load(Kubernetes(), AnswerVault);

        Assert.Equal("kc-secret", configuration["Keycloak:ClientSecret"]);
        Assert.Contains($"POST {Address}/v1/auth/kubernetes/login", requested);
        Assert.DoesNotContain(requested, request => request.Contains("approle", StringComparison.Ordinal));
        JsonElement login = Assert.Single(logins);
        Assert.Equal("dispute-api", login.GetProperty("role").GetString());
        Assert.Equal(Jwt, login.GetProperty("jwt").GetString());
    }

    [Fact]
    public void KubernetesLoginUsesTheConfiguredMountPoint()
    {
        File.WriteAllText(TokenFile, Jwt);

        Load(Kubernetes(mountPoint: "k8s-homelab"), AnswerVault);

        Assert.Contains($"POST {Address}/v1/auth/k8s-homelab/login", requested);
        Assert.DoesNotContain($"POST {Address}/v1/auth/kubernetes/login", requested);
    }

    // The kubelet rotates the projected token, so a retry must send the current file, not the one first read.
    [Fact]
    public void TheTokenIsReReadOnEveryAttempt()
    {
        File.WriteAllText(TokenFile, "token-before-rotation");
        bool rotated = false;

        Load(Kubernetes(), request =>
        {
            if (!rotated && request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
            {
                rotated = true;
                File.WriteAllText(TokenFile, "token-after-rotation");
                return Track(request, new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return AnswerVault(request);
        });

        Assert.Equal(["token-before-rotation", "token-after-rotation"], logins.Select(login => login.GetProperty("jwt").GetString()));
    }

    // The token may not be mounted yet when the container starts; a missing file is retried, not fatal.
    [Fact]
    public async Task AMissingTokenFileIsRetriedUntilItAppears()
    {
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        Task mounted = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            await File.WriteAllTextAsync(TokenFile, Jwt);
        });

        IConfiguration configuration = Load(Kubernetes(firstRetryDelay: TimeSpan.FromSeconds(1)), AnswerVault);
        await mounted;

        Assert.Equal("kc-secret", configuration["Keycloak:ClientSecret"]);
        Assert.Equal(Jwt, Assert.Single(logins).GetProperty("jwt").GetString());
        // At least one backoff happened, so the first read really found no file.
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(900), $"elapsed {elapsed.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public void AnEmptyTokenFileIsRetriedThenReportedWithoutARequest()
    {
        File.WriteAllText(TokenFile, " \n");

        VaultUnavailableException failure = Assert.Throws<VaultUnavailableException>(() => Load(Kubernetes(), AnswerVault));

        Assert.Contains("Kubernetes login after 3 attempts", failure.Message, StringComparison.Ordinal);
        Assert.Empty(requested);
    }

    [Fact]
    public void KubernetesWithoutARoleFailsAtOnceWithoutARequest()
    {
        File.WriteAllText(TokenFile, Jwt);
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();

        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() =>
            Load(Kubernetes(role: " ", firstRetryDelay: TimeSpan.FromSeconds(5)), AnswerVault));

        Assert.Contains("KubernetesRole", failure.Message, StringComparison.Ordinal);
        Assert.Empty(requested);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"elapsed {elapsed.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public void AnUnknownAuthMethodFailsAtOnceWithoutARequest()
    {
        File.WriteAllText(Path.Combine(directory, "role_id"), "role-123");
        File.WriteAllText(Path.Combine(directory, "secret_id"), "secret-456");
        VaultSettings settings = new()
        {
            Address = Address,
            AuthMethod = (VaultAuthMethod)42,
            RoleIdFile = Path.Combine(directory, "role_id"),
            SecretIdFile = Path.Combine(directory, "secret_id"),
            FirstRetryDelay = TimeSpan.FromSeconds(5),
        };

        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() => Load(settings, AnswerVault));

        Assert.Contains("not supported", failure.Message, StringComparison.Ordinal);
        Assert.Empty(requested);
    }

    // A typo in the method name fails in the binder, before the provider; it is reported like any other bad setting.
    [Fact]
    public void AnUnknownAuthMethodNameIsReportedAsMisconfiguration()
    {
        ConfigurationManager configuration = new();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Vault:Address"] = Address,
            ["Vault:AuthMethod"] = "Kubernets",
            ["Vault:Attempts"] = "1",
        });

        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() => configuration.AddVaultSecrets(migrate: false));

        Assert.Contains("Vault:AuthMethod", failure.Message, StringComparison.Ordinal);
    }

    // Vault refusing the login (role not bound to this service account) must not echo the token anywhere.
    [Fact]
    public void NoExceptionMessageContainsTheToken()
    {
        File.WriteAllText(TokenFile, Jwt);

        VaultUnavailableException failure = Assert.Throws<VaultUnavailableException>(() => Load(Kubernetes(), request =>
            Track(request, new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { errors = new[] { "permission denied" } }) })));

        Assert.Contains("Kubernetes login after 3 attempts", failure.Message, StringComparison.Ordinal);
        for (Exception? exception = failure; exception is not null; exception = exception.InnerException)
        {
            Assert.DoesNotContain(Jwt, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Jwt, exception.ToString(), StringComparison.Ordinal);
        }
    }

    // The cluster manifest sets Vault__AuthMethod, Vault__KubernetesRole and Vault__JwtFile; the rest keep their defaults.
    [Fact]
    public void TheClusterSettingsBindFromConfiguration()
    {
        VaultSettings settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:AuthMethod"] = "Kubernetes",
                ["Vault:KubernetesRole"] = "dispute-api",
            })
            .Build()
            .GetSection(VaultSettings.Section)
            .Get<VaultSettings>()!;

        Assert.Equal(VaultAuthMethod.Kubernetes, settings.AuthMethod);
        Assert.Equal("dispute-api", settings.KubernetesRole);
        Assert.Equal("/var/run/secrets/kubernetes.io/serviceaccount/token", settings.JwtFile);
        Assert.Equal("kubernetes", settings.KubernetesMountPoint);
        Assert.Equal(VaultAuthMethod.AppRole, new VaultSettings().AuthMethod);
    }

    private string TokenFile => Path.Combine(directory, "token");

    private VaultSettings Kubernetes(string role = "dispute-api", string mountPoint = "kubernetes", TimeSpan? firstRetryDelay = null) => new()
    {
        Address = Address,
        AuthMethod = VaultAuthMethod.Kubernetes,
        KubernetesRole = role,
        KubernetesMountPoint = mountPoint,
        JwtFile = TokenFile,
        Attempts = 3,
        FirstRetryDelay = firstRetryDelay ?? TimeSpan.FromMilliseconds(1),
    };

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
        return Load(settings, respond, migrate);
    }

    private static IConfiguration Load(VaultSettings settings, Func<HttpRequestMessage, HttpResponseMessage> respond, bool migrate = false) =>
        new ConfigurationBuilder()
            .Add(new VaultConfigurationSource(settings, VaultSecretPath.For(migrate)) { HttpMessageHandler = new Responder(respond) })
            .Build();

    private HttpResponseMessage AnswerVault(HttpRequestMessage request) => request.RequestUri!.AbsolutePath switch
    {
        "/v1/auth/approle/login" or "/v1/auth/kubernetes/login" or "/v1/auth/k8s-homelab/login" => Track(request, Json(new { auth = new { client_token = "token", lease_duration = 60, renewable = false, policies = Array.Empty<string>() } })),
        "/v1/kv/data/dispute-portal/database" => Track(request, Secret(new() { ["Host"] = "postgres", ["Port"] = "5432", ["Name"] = "dispute_portal", ["Username"] = "dispute_app", ["Password"] = "app-pass" })),
        "/v1/kv/data/dispute-portal/database-owner" => Track(request, Secret(new() { ["Host"] = "postgres", ["Username"] = "dispute_owner", ["Password"] = "owner-pass" })),
        "/v1/kv/data/dispute-portal/redis" => Track(request, Secret(new() { ["Host"] = "redis", ["Username"] = "dispute_app", ["Password"] = "redis-pass" })),
        "/v1/kv/data/dispute-portal/keycloak" => Track(request, Secret(new() { ["ClientSecret"] = "kc-secret" })),
        _ => Track(request, new HttpResponseMessage(HttpStatusCode.Forbidden)),
    };

    private HttpResponseMessage Track(HttpRequestMessage request, HttpResponseMessage response)
    {
        requested.Add(request.Method + " " + request.RequestUri!.GetLeftPart(UriPartial.Path));
        if (request.RequestUri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
        {
            logins.Add(JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.Clone());
        }

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
