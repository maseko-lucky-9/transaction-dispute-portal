using System.Globalization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// The refresh_token grant against Keycloak's back-channel token endpoint, with the OIDC handler's own client
/// credentials, metadata and token validation, so a refreshed ID token is checked exactly like the first one.
/// </summary>
public sealed class KeycloakTokenRefresher(
    HttpClient http,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    TimeProvider time,
    ILogger<KeycloakTokenRefresher> logger) : ITokenRefresher
{
    public async Task<RefreshedSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        OpenIdConnectOptions options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        try
        {
            OpenIdConnectConfiguration configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
            OpenIdConnectMessage? response = await RedeemAsync(configuration.TokenEndpoint, options, refreshToken, cancellationToken);
            if (response is null || string.IsNullOrEmpty(response.IdToken))
            {
                return null;
            }

            TokenValidationParameters parameters = options.TokenValidationParameters.Clone();
            parameters.IssuerSigningKeys = configuration.SigningKeys;
            TokenValidationResult validated = await options.TokenHandler.ValidateTokenAsync(response.IdToken, parameters);
            if (!validated.IsValid)
            {
                logger.LogWarning("The refreshed ID token failed validation: {Reason}", validated.Exception?.GetType().Name);
                return null;
            }

            int lifetimeSeconds = int.TryParse(response.ExpiresIn, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) ? seconds : 0;
            return new RefreshedSession(
                validated.ClaimsIdentity,
                response.IdToken,
                string.IsNullOrEmpty(response.RefreshToken) ? refreshToken : response.RefreshToken,
                time.GetUtcNow().AddSeconds(lifetimeSeconds));
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException or InvalidOperationException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // InvalidOperationException: the metadata could not be fetched. Every failure here ends the session (fail closed).
            logger.LogWarning("Token refresh failed: {Reason}", exception.GetType().Name);
            return null;
        }
    }

    private async Task<OpenIdConnectMessage?> RedeemAsync(
        string tokenEndpoint, OpenIdConnectOptions options, string refreshToken, CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            [OpenIdConnectParameterNames.GrantType] = OpenIdConnectGrantTypes.RefreshToken,
            [OpenIdConnectParameterNames.RefreshToken] = refreshToken,
            [OpenIdConnectParameterNames.ClientId] = options.ClientId!,
            [OpenIdConnectParameterNames.ClientSecret] = options.ClientSecret!,
        });
        using HttpResponseMessage response = await http.PostAsync(new Uri(tokenEndpoint), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // invalid_grant: the Keycloak session ended or the user was disabled. The body may echo tokens, so it is not logged.
            logger.LogWarning("The token endpoint refused the refresh with {Status}", (int)response.StatusCode);
            return null;
        }

        return new OpenIdConnectMessage(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
