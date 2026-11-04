using System.Net;
using DisputePortal.Api.Features.Bff;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace DisputePortal.IntegrationTests;

/// <summary>Drives the real sign-in: /bff/login, the identity provider's redirect back to /signin-oidc, the session cookie.</summary>
public sealed record BrowserSession(string Cookie, SetCookieHeaderValue SessionSetCookie, Uri AuthorizeRequest, HttpResponseMessage Callback)
{
    /// <param name="cookieJar">Cookies the browser already holds (e.g. an earlier session), sent on every step like a real tab.</param>
    public static async Task<BrowserSession> SignInAsync(ApiFactory api, IdpUser user, string returnUrl = "/", string? cookieJar = null)
    {
        using HttpClient browser = api.CreateBrowserClient();
        using HttpRequestMessage login = new(HttpMethod.Get, "/bff/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        if (cookieJar is not null)
        {
            login.Headers.Add(HeaderNames.Cookie, cookieJar);
        }

        HttpResponseMessage challenge = await browser.SendAsync(login);
        Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
        Uri authorize = challenge.Headers.Location!;
        Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(authorize.Query);

        string code = Guid.NewGuid().ToString("N");
        api.IdentityProvider.Approve(code, user, query["nonce"].ToString(), query["code_challenge"].ToString());

        using HttpRequestMessage callback = new(HttpMethod.Get, "/signin-oidc?code=" + code + "&state=" + Uri.EscapeDataString(query["state"].ToString()));
        callback.Headers.Add(HeaderNames.Cookie, string.Join("; ", new[] { cookieJar, CookieHeader(challenge) }.Where(part => !string.IsNullOrEmpty(part))));
        HttpResponseMessage signedIn = await browser.SendAsync(callback);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Equal(returnUrl, signedIn.Headers.Location?.OriginalString);

        SetCookieHeaderValue session = SetCookies(signedIn).Single(cookie => cookie.Name == BffAuthentication.CookieName);
        return new BrowserSession(session.Name + "=" + session.Value, session, authorize, signedIn);
    }

    /// <summary>A request carrying this session's cookie, and the CSRF header unless a test leaves it out.</summary>
    public HttpRequestMessage Request(HttpMethod method, string path, bool csrf = true)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add(HeaderNames.Cookie, Cookie);
        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        return request;
    }

    public static IList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.SetCookie, out IEnumerable<string>? values)
            ? SetCookieHeaderValue.ParseList([.. values])
            : [];

    private static string CookieHeader(HttpResponseMessage response) =>
        string.Join("; ", SetCookies(response).Select(cookie => cookie.Name + "=" + cookie.Value));
}
