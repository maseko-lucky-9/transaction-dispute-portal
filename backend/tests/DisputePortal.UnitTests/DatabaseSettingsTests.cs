using DisputePortal.Api.Shared.Persistence;
using Npgsql;

namespace DisputePortal.UnitTests;

public sealed class DatabaseSettingsTests
{
    private static readonly DatabaseSettings Settings = new() { Host = "postgres", Name = "dispute_portal", Username = "user", Password = "pw" };

    [Fact]
    public void TheApiConnectionBoundsEveryStatement()
    {
        NpgsqlConnectionStringBuilder api = new(Settings.ToConnectionString(DatabaseUse.Application));

        Assert.Equal("-c statement_timeout=5000", api.Options);
    }

    // A long index build in a future migration must not be cut off at 5 s.
    [Fact]
    public void TheMigrationConnectionHasNoStatementTimeout()
    {
        NpgsqlConnectionStringBuilder migrations = new(Settings.ToConnectionString(DatabaseUse.Migrations));

        Assert.True(string.IsNullOrEmpty(migrations.Options));
    }

    // The API fails fast so an unhealthy replica is replaced; the one-shot migrate job waits out a busy host.
    [Fact]
    public void TheMigrationConnectionWaitsLongerToOpenThanTheApi()
    {
        Assert.Equal(5, new NpgsqlConnectionStringBuilder(Settings.ToConnectionString(DatabaseUse.Application)).Timeout);
        Assert.Equal(30, new NpgsqlConnectionStringBuilder(Settings.ToConnectionString(DatabaseUse.Migrations)).Timeout);
    }

    // Three replicas at 20 stay under Postgres's default max_connections of 100.
    [Fact]
    public void ThePoolDefaultsTo20AndIsConfigurable()
    {
        Assert.Equal(20, new NpgsqlConnectionStringBuilder(Settings.ToConnectionString(DatabaseUse.Application)).MaxPoolSize);
        DatabaseSettings larger = new() { Host = "postgres", Name = "dispute_portal", Username = "user", Password = "pw", MaxPoolSize = 40 };
        Assert.Equal(40, new NpgsqlConnectionStringBuilder(larger.ToConnectionString(DatabaseUse.Migrations)).MaxPoolSize);
    }

    [Fact]
    public void TheConnectionCarriesTheConfiguredServerAndRole()
    {
        NpgsqlConnectionStringBuilder api = new(Settings.ToConnectionString(DatabaseUse.Application));

        Assert.Equal(("postgres", 5432, "dispute_portal", "user"), (api.Host, api.Port, api.Database, api.Username));
    }
}
