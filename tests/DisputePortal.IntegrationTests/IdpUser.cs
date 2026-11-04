namespace DisputePortal.IntegrationTests;

/// <summary>A user of the fake identity provider: what Keycloak would put in the ID token.</summary>
public sealed record IdpUser(string Subject, string Name, IReadOnlyList<string> Roles)
{
    /// <summary>Keycloak's session id (the ID token's sid): two instances of one user are two Keycloak sessions.</summary>
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
}
