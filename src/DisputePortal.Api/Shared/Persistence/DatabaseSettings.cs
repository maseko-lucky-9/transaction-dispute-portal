using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace DisputePortal.Api.Shared.Persistence;

/// <summary>
/// The connection, as Vault delivers it (kv/dispute-portal/database). The migrate mode binds the same shape from
/// <c>DatabaseOwner</c>; the API never sees the owner's credentials.
/// </summary>
public sealed class DatabaseSettings
{
    public const string AppSection = "Database";
    public const string OwnerSection = "DatabaseOwner";

    private const int ConnectTimeoutSeconds = 5;

    // Npgsql's type-loading query on open counts against this timeout. The API fails fast (5 s) so a replica reports
    // unhealthy and is replaced; the one-shot migrate job has no such alternative and runs while the host is busy
    // building and recreating containers, so it waits longer instead of failing the whole `up`.
    private const int MigrationConnectTimeoutSeconds = 30;

    // statement_timeout bounds a runaway API query; migrations run without it, so a long index build can finish.
    private const int StatementTimeoutMilliseconds = 5000;

    [Required]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 5432;

    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Username { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Connections per replica. 20 keeps 3 replicas (60) plus Keycloak and the migrate job under Postgres's default
    /// max_connections of 100; raise it together with max_connections.
    /// </summary>
    [Range(1, 200)]
    public int MaxPoolSize { get; init; } = 20;

    public string ToConnectionString(DatabaseUse use)
    {
        NpgsqlConnectionStringBuilder builder = new()
        {
            Host = Host,
            Port = Port,
            Database = Name,
            Username = Username,
            Password = Password,
            MaxPoolSize = MaxPoolSize,
            Timeout = use == DatabaseUse.Migrations ? MigrationConnectTimeoutSeconds : ConnectTimeoutSeconds,
            ApplicationName = use == DatabaseUse.Migrations ? "dispute-portal-migrate" : "dispute-portal",
        };
        if (use == DatabaseUse.Application)
        {
            builder.Options = $"-c statement_timeout={StatementTimeoutMilliseconds}";
        }

        return builder.ConnectionString;
    }
}
