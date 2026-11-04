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

    // Capacity baseline: 50 connections per replica; statement_timeout bounds a runaway query.
    private const int MaxPoolSize = 50;
    private const int ConnectTimeoutSeconds = 5;
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

    public string ToConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Name,
        Username = Username,
        Password = Password,
        MaxPoolSize = MaxPoolSize,
        Timeout = ConnectTimeoutSeconds,
        Options = $"-c statement_timeout={StatementTimeoutMilliseconds}",
        ApplicationName = "dispute-portal",
    }.ConnectionString;
}
