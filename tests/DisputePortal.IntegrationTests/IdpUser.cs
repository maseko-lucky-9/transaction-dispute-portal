namespace DisputePortal.IntegrationTests;

/// <summary>A user of the fake identity provider: what Keycloak would put in the ID token.</summary>
public sealed record IdpUser(string Subject, string Name, IReadOnlyList<string> Roles)
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
}
