using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// Backend-for-frontend: the API is the OIDC confidential client (code + PKCE S256). The browser holds only an
/// HttpOnly session cookie; tokens stay server-side in an encrypted Redis ticket.
/// </summary>
public static class BffAuthentication
{
    public const string CookieName = "dp_session";

    // __Host- binds the cookie to the exact origin, but browsers accept it only over https.
    public const string HostPrefixedCookieName = "__Host-" + CookieName;

    public static IServiceCollection AddBffAuthentication(this IServiceCollection services)
    {
        services.AddOptions<PublicSettings>().BindConfiguration(PublicSettings.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<KeycloakSettings>().BindConfiguration(KeycloakSettings.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<RefreshSingleFlight>();
        services.AddScoped<SessionValidator>();
        services.AddScoped<BffCookieEvents>();
        services.AddScoped<BffOidcEvents>();
        // No retries on the POST: Keycloak rotates refresh tokens with reuse 0, so replaying one would revoke the session.
        services.AddHttpClient<ITokenRefresher, KeycloakTokenRefresher>()
            .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect();
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<ITicketStore, IOptions<PublicSettings>>(ConfigureCookie);
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<KeycloakSettings>>((options, keycloak) => ConfigureOidc(options, keycloak.Value));
        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, ITicketStore store, IOptions<PublicSettings> publicSettings)
    {
        options.Cookie.Name = publicSettings.Value.IsHttps ? HostPrefixedCookieName : CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = SessionTokens.IdleTimeout;
        options.SlidingExpiration = true;
        options.SessionStore = store;
        options.EventsType = typeof(BffCookieEvents);
    }

    private static void ConfigureOidc(OpenIdConnectOptions options, KeycloakSettings keycloak)
    {
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.Authority = keycloak.Authority;
        if (!string.IsNullOrWhiteSpace(keycloak.MetadataAddress))
        {
            options.MetadataAddress = keycloak.MetadataAddress;
            // Only because discovery runs over the internal compose network; the issuer is still validated below.
            options.RequireHttpsMetadata = !keycloak.MetadataAddress.StartsWith(Uri.UriSchemeHttp + "://", StringComparison.OrdinalIgnoreCase);
        }

        options.ClientId = keycloak.ClientId;
        options.ClientSecret = keycloak.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        // Query, not form_post: the return is a top-level GET, so SameSite=Lax correlation cookies are sent.
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.Scope.Clear();
        options.Scope.Add(OpenIdConnectScope.OpenId);
        options.Scope.Add("profile");
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.DisableTelemetry = true;
        options.TokenValidationParameters.ValidIssuer = keycloak.Authority;
        options.TokenValidationParameters.NameClaimType = ClaimNames.Name;
        options.TokenValidationParameters.RoleClaimType = ClaimNames.Roles;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.EventsType = typeof(BffOidcEvents);
    }
}
