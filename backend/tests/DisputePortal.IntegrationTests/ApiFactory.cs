using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using DisputePortal.Api.Shared.Eligibility;
using DisputePortal.Api.Shared.OpenApi;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DotNet.Testcontainers.Containers;
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

    /// <summary>The management port under the compose alias Keycloak uses for back-channel logout.</summary>
    public const string InternalHost = "api-mgmt";
    public const string ManagementBaseUrl = "http://" + InternalHost + ":8081";
    public const int ManagementPort = 8081;

    /// <summary>The compose data network, and Keycloak's address on it: the peer a management-port request comes from by default.</summary>
    public const string InternalNetwork = "10.231.28.0/24";
    public const string KeycloakPeer = "10.231.28.20";

    /// <summary>The compose ACL user (deploy/bootstrap/bootstrap.sh), so every Redis command the app sends is proven under it.</summary>
    public const string RedisUser = "dispute_api";

    private const string SetPasswordSql = "SELECT format('ALTER ROLE %I PASSWORD %L', @role, @password)";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase(DatabaseName).Build();
    private readonly RedisContainer redis = new RedisBuilder("redis:7-alpine").Build();

    // Throwaway secrets, new every run and never printed.
    private readonly string appPassword = Secret();
    private readonly string ownerPassword = Secret();
    private readonly string redisPassword = Secret();

    public FakeIdentityProvider IdentityProvider { get; } = new(Secret());

    public RedisContainer Redis => redis;

    public string AppConnectionString => RoleConnectionString(AppRole, appPassword);

    /// <summary>The role that owns the schema (what migrations and a core-banking feed would write as).</summary>
    public string OwnerConnectionString => RoleConnectionString(OwnerRole, ownerPassword);

    /// <summary>The container's superuser on the app database: test-data setup only, never what the API connects as.</summary>
    public string AdminConnectionString => postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
        await PrepareRedisUserAsync();
        await PrepareRolesAsync();
        Assert.Equal(0, await MigrateAsync(DatabaseName, OwnerRole, ownerPassword, TimeProvider.System));
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
        builder.UseSetting("Redis:Username", RedisUser);
        builder.UseSetting("Redis:Password", redisPassword);
        builder.UseSetting("Public:BaseUrl", PublicBaseUrl);
        builder.UseSetting("Keycloak:Authority", FakeIdentityProvider.Issuer);
        builder.UseSetting("Keycloak:ClientSecret", IdentityProvider.ClientSecret);
        builder.UseSetting("Forwarded:KnownNetwork", "172.28.0.0/16");
        // As compose: Keycloak reaches the management port under this name, from the data network (ADR-011).
        builder.UseSetting("Ports:InternalHost", InternalHost);
        builder.UseSetting("Ports:InternalNetwork", InternalNetwork);
        // Off by default everywhere, so the suite that reads the document turns it on; SwaggerUiTests derives hosts without it.
        builder.UseSetting(ApiDocumentation.EnabledSetting, "true");
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

    /// <summary>
    /// A second host on this fixture's Redis and Keycloak whose database has gone away, as it does for a replica that is
    /// already serving: the host starts through a <see cref="TcpRelay"/> (Program waits for Postgres before it listens, so a
    /// host that began with no database would never start), loads its Data Protection key ring, which lives in Postgres and
    /// which a replica keeps cached for a day, and then the relay is cut. The data source now points at a closed loopback
    /// port, refused at once, and EF keeps its retrying strategy with zero retries, so the first transient failure surfaces
    /// as the <see cref="Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException"/> production maps to 503
    /// instead of sitting out the three-retry back-off. Sessions signed in on this fixture's own host are valid on it
    /// (same Redis, same key ring). Dispose the returned host.
    /// </summary>
    public WebApplicationFactory<Program> WithUnreachableDatabase()
    {
        using TcpRelay relay = new(postgres.Hostname, postgres.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort));
        WebApplicationFactory<Program> offline = WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:Host", IPAddress.Loopback.ToString());
            builder.UseSetting("Database:Port", relay.Port.ToString(CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<DisputePortalDbContext>(options => options.UseNpgsql(npgsql => npgsql.EnableRetryOnFailure(0))));
        });
        // Touching Services starts the host, which passes the startup gate through the relay.
        offline.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("warm-up").Protect([0]);
        relay.Cut();
        // Idle connections opened through the relay are dead; drop them so every query meets the closed port.
        offline.Services.GetRequiredService<NpgsqlDataSource>().Clear();
        return offline;
    }

    /// <summary>
    /// Takes Redis commands away from the app's ACL user, or gives them back (each was allowed by the compose rule below),
    /// to make one kind of Redis call fail while the others work. ACL changes apply to open connections at once.
    /// </summary>
    public async Task SetRedisCommandsAsync(bool allowed, params string[] commands)
    {
        ExecResult result = await redis.ExecAsync(
            ["redis-cli", "ACL", "SETUSER", RedisUser, .. commands.Select(command => (allowed ? "+" : "-") + command)]);
        Assert.Equal("OK", result.Stdout.Trim());
    }

    // The same rule as compose's users.acl: bff:* keys only, no channels, no admin or dangerous commands.
    private async Task PrepareRedisUserAsync()
    {
        ExecResult result = await redis.ExecAsync(
        [
            "redis-cli", "ACL", "SETUSER", RedisUser, "on", ">" + redisPassword, "~bff:*", "resetchannels",
            "+@all", "-@admin", "-@dangerous", "+info", "+client|setname", "+client|setinfo", "+client|id",
        ]);
        Assert.Equal("OK", result.Stdout.Trim());
    }

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

    /// <summary>
    /// The --migrate code path itself (MigrationRunner, which also seeds), as the compose migrate job runs it. The fixture
    /// uses the owner's credentials; the seeder test points it at a fresh database with a fixed clock.
    /// </summary>
    public async Task<int> MigrateAsync(string database, string username, string password, TimeProvider clock)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseOwner:Host"] = postgres.Hostname,
                ["DatabaseOwner:Port"] = postgres.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort).ToString(CultureInfo.InvariantCulture),
                ["DatabaseOwner:Name"] = database,
                ["DatabaseOwner:Username"] = username,
                ["DatabaseOwner:Password"] = password,
            })
            .Build();
        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddPersistence(DatabaseUse.Migrations);
        services.AddDataProtectionKeyRing();
        services.AddDisputeEligibility();
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return await MigrationRunner.RunAsync(provider, CancellationToken.None);
    }

    /// <summary>A new, empty database on the same server, reachable with <see cref="AdminConnectionString"/>'s superuser.</summary>
    public async Task<(string Name, string Username, string Password)> CreateEmptyDatabaseAsync()
    {
        string name = "t" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection connection = new(AdminConnectionString);
        await connection.OpenAsync();
        // CREATE DATABASE takes no parameters; the name is generated above, never input.
        await using NpgsqlCommand command = new("CREATE DATABASE " + name, connection);
        await command.ExecuteNonQueryAsync();
        NpgsqlConnectionStringBuilder admin = new(AdminConnectionString);
        return (name, admin.Username!, admin.Password!);
    }

    /// <summary>A connection string for another database on the same server, as the superuser.</summary>
    public string AdminConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = database }.ConnectionString;

    private string RoleConnectionString(string role, string password) =>
        new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Username = role, Password = password, Database = DatabaseName }.ConnectionString;

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), redis.DisposeAsync().AsTask());
    }
}
