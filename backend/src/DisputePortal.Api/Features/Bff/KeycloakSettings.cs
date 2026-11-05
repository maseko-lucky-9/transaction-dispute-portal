using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// The confidential client. <see cref="Authority"/> is the public issuer the browser sees; <see cref="MetadataAddress"/>
/// is the internal discovery URL. Keycloak's dynamic back-channel hostname makes the metadata advertise public
/// front-channel endpoints and an internal token endpoint. <see cref="ClientSecret"/> comes from Vault.
/// </summary>
public sealed class KeycloakSettings
{
    public const string Section = "Keycloak";

    [Required]
    [Url]
    public string Authority { get; init; } = string.Empty;

    public string? MetadataAddress { get; init; }

    [Required]
    public string ClientId { get; init; } = "dispute-portal-bff";

    [Required]
    public string ClientSecret { get; init; } = string.Empty;
}
