using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// The cookie scheme never redirects: a missing session is a JSON 401 and a refusal a JSON 403, so the SPA's fetch
/// sees a status, not Keycloak's login page. Only GET /bff/login starts the OIDC challenge.
/// </summary>
public sealed class BffCookieEvents(SessionValidator sessionValidator) : CookieAuthenticationEvents
{
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        ProblemResponses.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.ForStatus(StatusCodes.Status401Unauthorized));

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        ProblemResponses.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.ForStatus(StatusCodes.Status403Forbidden));

    public override Task ValidatePrincipal(CookieValidatePrincipalContext context) => sessionValidator.ValidateAsync(context);
}
