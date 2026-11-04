using DisputePortal.Api.Shared.Errors;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace DisputePortal.Api.Features.Bff;

/// <summary>Keycloak's discovery document and signing keys, from the login handler's own configuration manager (one cache, one trust root).</summary>
public static class IdentityProviderMetadata
{
    /// <summary>
    /// A cached configuration is returned even while a refresh of it fails, so only a cold miss throws, and it becomes a
    /// 503 <c>idp_unavailable</c> instead of a 500.
    /// </summary>
    public static async Task<OpenIdConnectConfiguration> GetAsync(OpenIdConnectOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new IdentityProviderUnavailableException(exception);
        }
    }
}
