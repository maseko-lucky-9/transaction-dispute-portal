namespace DisputePortal.Api.Shared.Errors;

/// <summary>Keycloak's metadata or token endpoint cannot be reached. Sign-in and session refresh fail closed (503), and the session is kept.</summary>
public sealed class IdentityProviderUnavailableException(Exception? cause = null)
    : ApiException("The identity provider is unavailable; retry shortly.", StatusCodes.Status503ServiceUnavailable, "idp_unavailable", cause);
