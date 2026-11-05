using System.Security.Claims;

namespace DisputePortal.Api.Shared.Security;

/// <param name="Identity">Claims of the new, validated ID token: roles are re-read from here.</param>
public sealed record RefreshedSession(ClaimsIdentity Identity, string IdToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);
