namespace DisputePortal.Api.Shared.Secrets;

/// <summary>One KV v2 secret and the configuration prefix its keys land under (e.g. <c>Database:Password</c>).</summary>
public sealed record VaultSecretPath(string Path, string ConfigurationPrefix)
{
    public static readonly VaultSecretPath Database = new("dispute-portal/database", "Database");
    public static readonly VaultSecretPath DatabaseOwner = new("dispute-portal/database-owner", "DatabaseOwner");
    public static readonly VaultSecretPath Redis = new("dispute-portal/redis", "Redis");
    public static readonly VaultSecretPath Keycloak = new("dispute-portal/keycloak", "Keycloak");

    /// <summary>
    /// The API's AppRole reads the app paths; the migrate AppRole reads only the owner path, so the API never
    /// holds DDL credentials.
    /// </summary>
    public static IReadOnlyList<VaultSecretPath> For(bool migrate) =>
        migrate ? [DatabaseOwner] : [Database, Redis, Keycloak];
}
