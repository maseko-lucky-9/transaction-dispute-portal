namespace DisputePortal.Api.Shared.Security;

/// <summary>Keycloak realm roles, delivered in the ID token's <c>roles</c> claim.</summary>
public static class Roles
{
    public const string Customer = "customer";
    public const string DisputeAgent = "dispute-agent";

    /// <summary>Only these reach the session; Keycloak's default roles (offline_access, ...) are dropped.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { Customer, DisputeAgent };
}
