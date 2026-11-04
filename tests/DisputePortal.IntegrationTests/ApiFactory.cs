using System.Globalization;
using System.Security.Cryptography;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// postgres:17-alpine and redis:7-alpine containers and one API host on the real auth stack, shared by the "api"
/// collection. The database is prepared as compose prepares it: deploy/postgres/roles.sql, role passwords, migrations
/// as dispute_owner. The API then connects as dispute_app.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabaseName = "dispute_portal";
    public const string AppRole = "dispute_app";
    public const string OwnerRole = "dispute_owner";
    public const string PublicBaseUrl = "http://localhost";

    private const string SetPasswordSql = "SELECT format('ALTER ROLE %I PASSWORD %L', @role, @password)";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase(DatabaseName).Build();
    private readonly RedisContainer redis = new RedisBuilder("redis:7-alpine").Build();

    // Throwaway secrets, new every run and never printed.
    private readonly string appPassword = Secret();
    private readonly string ownerPassword = Secret();

    public FakeIdentityProvider IdentityProvider { get; } = new(Secret());

    public RedisContainer Redis => redis;

    public string AppConnectionString => RoleConnectionString(AppRole, appPassword);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
        await PrepareRolesAsync();
        await MigrateAsOwnerAsync();
    }

    // UseSetting reaches Program's configuration before Build(); hosts derived with WithWebHostBuilder inherit it.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Host", postgres.Hostname);
        builder.UseSetting("Database:Port", postgres.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort).ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Database:Name", DatabaseName);
        builder.UseSetting("Database:Username", AppRole);
        builder.UseSetting("Database:Password", appPassword);
        builder.UseSetting("Redis:Host", redis.Hostname);
        builder.UseSetting("Redis:Port", redis.GetMappedPublicPort(RedisBuilder.RedisPort).ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Public:BaseUrl", PublicBaseUrl);
        builder.UseSetting("Keycloak:Authority", FakeIdentityProvider.Issuer);
        builder.UseSetting("Keycloak:ClientSecret", IdentityProvider.ClientSecret);
        builder.UseSetting("Forwarded:KnownNetwork", "172.28.0.0/16");
        builder.ConfigureTestServices(services =>
        {
            // The real OIDC handler, with metadata supplied in memory and the back channel answered by the fake.
            services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = IdentityProvider.Configuration;
                options.BackchannelHttpHandler = IdentityProvider.Handler;
            });
            services.AddHttpClient<ITokenRefresher, KeycloakTokenRefresher>()
                .ConfigurePrimaryHttpMessageHandler(() => IdentityProvider.Handler);
            services.AddSingleton<IStartupFilter, KestrelPortEmulation>();
        });
    }

    /// <summary>A client that behaves like a browser tab with cookies off: no redirects followed, cookies sent by hand.</summary>
    public HttpClient CreateBrowserClient(string baseAddress = PublicBaseUrl) =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri(baseAddress) });

    private async Task PrepareRolesAsync()
    {
        await using NpgsqlConnection connection = new(postgres.GetConnectionString());
        await connection.OpenAsync();
        string rolesScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "roles.sql"));
        await using (NpgsqlCommand roles = new(rolesScript, connection))
        {
            await roles.ExecuteNonQueryAsync();
        }

        foreach ((string role, string password) in new[] { (AppRole, appPassword), (OwnerRole, ownerPassword) })
        {
            await using NpgsqlCommand format = new(SetPasswordSql, connection);
            format.Parameters.AddWithValue("role", role);
            format.Parameters.AddWithValue("password", password);
            string alterRole = (string)(await format.ExecuteScalarAsync())!;
            await using NpgsqlCommand alter = new(alterRole, connection);
            await alter.ExecuteNonQueryAsync();
        }
    }

    // The --migrate code path itself (MigrationRunner), on the owner's credentials, as the compose migrate job runs it.
    private async Task MigrateAsOwnerAsync()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseOwner:Host"] = postgres.Hostname,
                ["DatabaseOwner:Port"] = postgres.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort).ToString(CultureInfo.InvariantCulture),
                ["DatabaseOwner:Name"] = DatabaseName,
                ["DatabaseOwner:Username"] = OwnerRole,
                ["DatabaseOwner:Password"] = ownerPassword,
            })
            .Build();
        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddPersistence(DatabaseSettings.OwnerSection);
        services.AddDataProtectionKeyRing();
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        int exitCode = await MigrationRunner.RunAsync(provider, CancellationToken.None);
        Assert.Equal(0, exitCode);
    }

    private string RoleConnectionString(string role, string password) =>
        new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Username = role, Password = password, Database = DatabaseName }.ConnectionString;

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), redis.DisposeAsync().AsTask());
    }
}
