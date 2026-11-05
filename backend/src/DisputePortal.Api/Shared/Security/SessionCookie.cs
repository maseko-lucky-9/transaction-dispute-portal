using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DisputePortal.Api.Shared.Security;

/// <summary>Reads the ticket-store key out of the request's session cookie, without authenticating it.</summary>
public static class SessionCookie
{
    /// <summary>The cookie handler's claim for the ticket-store key (CookieAuthenticationHandler.SessionIdClaim, which is private).</summary>
    public const string SessionKeyClaimType = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    /// <returns>Null when there is no cookie, or it is expired, forged or from an old key ring.</returns>
    public static string? ReadKey(HttpContext context, CookieAuthenticationOptions options)
    {
        string? cookie = options.CookieManager.GetRequestCookie(context, options.Cookie.Name!);
        if (string.IsNullOrEmpty(cookie))
        {
            return null;
        }

        AuthenticationTicket? ticket = options.TicketDataFormat.Unprotect(cookie);
        return ticket?.Principal.FindFirst(SessionKeyClaimType)?.Value;
    }
}
