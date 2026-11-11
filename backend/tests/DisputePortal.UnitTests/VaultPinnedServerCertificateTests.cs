using System.Net;
using System.Security.Cryptography.X509Certificates;
using DisputePortal.Api.Shared.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace DisputePortal.UnitTests;

/// <summary>
/// The pin wired into VaultSharp's real HTTP stack: a Kestrel HTTPS server on 127.0.0.1 with a self-signed certificate
/// (no CA can vouch for it, as on the cluster) answers the same login and KV responses as the in-memory fake.
/// </summary>
public sealed class VaultPinnedServerCertificateTests : IAsyncLifetime
{
    private const string Jwt = "test-service-account-token-7f3c9a";

    private readonly string directory = Directory.CreateTempSubdirectory("vault-tls-test").FullName;
    private readonly X509Certificate2 serverCertificate = TestCertificates.Server();
    private readonly List<string> requested = [];
    private WebApplication? server;
    private string address = string.Empty;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(serverCertificate)));
        server = builder.Build();
        server.Run(async context =>
        {
            requested.Add(context.Request.Method + " " + context.Request.Path);
            using HttpResponseMessage answer = FakeVaultResponses.For(context.Request.Path);
            context.Response.StatusCode = (int)answer.StatusCode;
            context.Response.ContentType = answer.Content.Headers.ContentType?.ToString();
            await answer.Content.CopyToAsync(context.Response.Body);
        });
        await server.StartAsync();
        address = server.Urls.Single();
        await File.WriteAllTextAsync(TokenFile, Jwt);
    }

    [Fact]
    public void TheCorrectPinLoadsTheSecretsOverKubernetesAuth()
    {
        IConfiguration configuration = Load(Kubernetes(Pin(serverCertificate)));

        Assert.StartsWith("https://127.0.0.1:", address, StringComparison.Ordinal);
        Assert.Equal("kc-secret", configuration["Keycloak:ClientSecret"]);
        Assert.Contains("POST /v1/auth/kubernetes/login", requested);
    }

    [Fact]
    public void TheCorrectPinLoadsTheSecretsOverAppRoleToo()
    {
        File.WriteAllText(Path.Combine(directory, "role_id"), "role-123");
        File.WriteAllText(Path.Combine(directory, "secret_id"), "secret-456");
        VaultSettings settings = new()
        {
            Address = address,
            RoleIdFile = Path.Combine(directory, "role_id"),
            SecretIdFile = Path.Combine(directory, "secret_id"),
            PinnedServerCertificateFile = Pin(serverCertificate),
            Attempts = 1,
        };

        IConfiguration configuration = Load(settings);

        Assert.Equal("redis-pass", configuration["Redis:Password"]);
        Assert.Contains("POST /v1/auth/approle/login", requested);
    }

    // The TLS rejection is the inner cause; the new startup detail names it instead of hiding it behind "unreachable".
    [Fact]
    public void AWrongPinFailsAndTheDetailNamesTheTlsRejection()
    {
        using X509Certificate2 other = TestCertificates.Server();

        VaultUnavailableException failure = Assert.Throws<VaultUnavailableException>(() => Load(Kubernetes(Pin(other))));

        string detail = VaultFailureDetail.Describe(failure);
        Assert.Contains("HttpRequestException", detail, StringComparison.Ordinal);
        Assert.Contains("AuthenticationException", detail, StringComparison.Ordinal);
        Assert.DoesNotContain(Jwt, detail, StringComparison.Ordinal);
        Assert.Empty(requested);
    }

    // Without a pin the system trust store decides, and it does not trust a self-signed server certificate.
    [Fact]
    public void WithoutAPinTheSelfSignedServerIsRejected()
    {
        VaultUnavailableException failure = Assert.Throws<VaultUnavailableException>(() => Load(Kubernetes(pinFile: string.Empty)));

        Assert.Contains("AuthenticationException", VaultFailureDetail.Describe(failure), StringComparison.Ordinal);
        Assert.Empty(requested);
    }

    // Security configuration: a pin file that cannot be read fails at once, before any request and without a retry.
    [Fact]
    public void AMissingPinFileFailsAtOnceWithoutARequest()
    {
        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() =>
            Load(Kubernetes(Path.Combine(directory, "absent.pem"))));

        Assert.Contains("PinnedServerCertificateFile", failure.Message, StringComparison.Ordinal);
        Assert.Empty(requested);
    }

    [Fact]
    public void AGarbagePinFileFailsAtOnceWithoutARequest()
    {
        string path = Path.Combine(directory, "garbage.pem");
        File.WriteAllText(path, "not a certificate");

        Assert.Throws<VaultMisconfiguredException>(() => Load(Kubernetes(path)));

        Assert.Empty(requested);
    }

    [Fact]
    public void APinFileWithTwoCertificatesFailsAtOnceWithoutARequest()
    {
        using X509Certificate2 other = TestCertificates.Server();
        string path = Path.Combine(directory, "bundle.pem");
        File.WriteAllText(path, serverCertificate.ExportCertificatePem() + "\n" + other.ExportCertificatePem());

        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() => Load(Kubernetes(path)));

        Assert.Contains("exactly one certificate", failure.Message, StringComparison.Ordinal);
        Assert.Empty(requested);
    }

    private string TokenFile => Path.Combine(directory, "token");

    private string Pin(X509Certificate2 certificate)
    {
        string path = Path.Combine(directory, $"pin-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }

    // One attempt: a wrong pin is retryable (HttpRequestException), so the retry would only slow the suite. A pin-file
    // failure is VaultMisconfiguredException, never the VaultUnavailableException a retryable error would end in.
    private VaultSettings Kubernetes(string pinFile) => new()
    {
        Address = address,
        AuthMethod = VaultAuthMethod.Kubernetes,
        KubernetesRole = "dispute-api",
        JwtFile = TokenFile,
        PinnedServerCertificateFile = pinFile,
        Attempts = 1,
    };

    // No HttpMessageHandler: the provider builds VaultSharp's real handler, as in production.
    private static IConfiguration Load(VaultSettings settings) =>
        new ConfigurationBuilder().Add(new VaultConfigurationSource(settings, VaultSecretPath.For(false))).Build();

    public async Task DisposeAsync()
    {
        if (server is not null)
        {
            await server.DisposeAsync();
        }

        serverCertificate.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
