namespace DisputePortal.Api.Shared.Persistence;

/// <summary>Who is connecting: the API (app role, bounded statements) or <c>--migrate</c> (owner role, long DDL allowed).</summary>
public enum DatabaseUse
{
    Application,
    Migrations,
}
