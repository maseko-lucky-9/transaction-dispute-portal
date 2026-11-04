using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// What the server-side ticket keeps besides the principal: the ID token (for logout's id_token_hint), the refresh
/// token, the access token's expiry and the session's absolute end. The access token itself is never stored.
/// </summary>
public static class SessionTokens
{
    public const string IdToken = "id_token";
    public const string RefreshToken = "refresh_token";
    public const string ExpiresAt = "expires_at";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);

    private const string AbsoluteExpiresAtItem = ".dp.absolute_expires_at";

    public static void Store(AuthenticationProperties properties, string idToken, string refreshToken, DateTimeOffset accessTokenExpiresAt) =>
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = IdToken, Value = idToken },
            new AuthenticationToken { Name = RefreshToken, Value = refreshToken },
            new AuthenticationToken { Name = ExpiresAt, Value = Format(accessTokenExpiresAt) },
        ]);

    public static DateTimeOffset? AccessTokenExpiresAt(AuthenticationProperties properties) =>
        Parse(properties.GetTokenValue(ExpiresAt));

    public static void StartAbsoluteLifetime(AuthenticationProperties properties, DateTimeOffset now) =>
        properties.Items[AbsoluteExpiresAtItem] = Format(now + AbsoluteLifetime);

    /// <summary>A ticket without the stamp counts as expired: fail closed.</summary>
    public static DateTimeOffset AbsoluteExpiresAt(AuthenticationProperties properties) =>
        Parse(properties.Items.TryGetValue(AbsoluteExpiresAtItem, out string? value) ? value : null) ?? DateTimeOffset.MinValue;

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : null;
}
