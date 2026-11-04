using System.Security.Claims;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// The session's principal: subject, display name, Keycloak session id and app roles only. Everything else in the
/// ID token is dropped, so the ticket in Redis holds the minimum of personal data.
/// </summary>
public static class SessionPrincipal
{
    public const string AuthenticationType = "dp_session";

    /// <param name="claims">Claims of a validated ID token.</param>
    /// <param name="previousSessionId">Kept when a refreshed ID token omits <c>sid</c>, so the logout link stays valid.</param>
    public static ClaimsPrincipal Create(IEnumerable<Claim> claims, string? previousSessionId = null)
    {
        List<Claim> source = [.. claims];
        string subject = Value(source, ClaimNames.Subject)
            ?? throw new InvalidOperationException("The ID token has no sub claim.");
        string name = Value(source, ClaimNames.Name) ?? Value(source, ClaimNames.PreferredUsername) ?? subject;
        string sessionId = Value(source, ClaimNames.SessionId) ?? previousSessionId ?? Guid.NewGuid().ToString("N");

        ClaimsIdentity identity = new(AuthenticationType, ClaimNames.Name, ClaimNames.Roles);
        identity.AddClaim(new Claim(ClaimNames.Subject, subject));
        identity.AddClaim(new Claim(ClaimNames.Name, name));
        identity.AddClaim(new Claim(ClaimNames.SessionId, sessionId));
        identity.AddClaims(source
            .Where(claim => claim.Type == ClaimNames.Roles && Roles.Known.Contains(claim.Value))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .Select(role => new Claim(ClaimNames.Roles, role)));
        return new ClaimsPrincipal(identity);
    }

    public static string Subject(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimNames.Subject) ?? throw new InvalidOperationException("The session has no sub claim.");

    public static IReadOnlyList<string> RolesOf(ClaimsPrincipal user) =>
        [.. user.FindAll(ClaimNames.Roles).Select(claim => claim.Value).Order(StringComparer.Ordinal)];

    private static string? Value(List<Claim> claims, string type) =>
        claims.FirstOrDefault(claim => claim.Type == type && !string.IsNullOrWhiteSpace(claim.Value))?.Value;
}
