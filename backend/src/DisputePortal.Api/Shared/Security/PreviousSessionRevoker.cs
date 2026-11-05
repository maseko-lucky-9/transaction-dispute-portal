using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// On a new sign-in, deletes the ticket behind the session cookie the browser still sends. The OIDC callback signs in
/// without the cookie scheme ever reading that cookie, so the cookie handler would store a new ticket and leave the old
/// one (with its refresh token) usable by anyone holding a copy of the old cookie.
/// </summary>
/// <remarks>
/// Reads the cookie directly instead of authenticating it: authenticating would run the session validator on the old
/// session, which could spend its refresh token at Keycloak or sign it out in the middle of the new sign-in.
/// </remarks>
public sealed class PreviousSessionRevoker(IOptionsMonitor<CookieAuthenticationOptions> cookieOptions, ITicketStore store)
{
    public async Task RevokeAsync(HttpContext context)
    {
        // An expired, forged or old-key-ring cookie reads as no key: nothing to revoke, and sign-in carries on.
        if (SessionCookie.ReadKey(context, cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme)) is { } key)
        {
            await store.RemoveAsync(key);
        }
    }
}
