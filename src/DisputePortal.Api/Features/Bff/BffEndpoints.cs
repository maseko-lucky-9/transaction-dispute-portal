using System.Security.Claims;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Api.Shared.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputePortal.Api.Features.Bff;

public static class BffEndpoints
{
    public const string LogoutPath = "/bff/logout";
    private const string Home = "/";

    public static WebApplication MapBffEndpoints(this WebApplication app)
    {
        RouteGroupBuilder bff = app.MapGroup("/bff").WithTags("Bff");
        bff.MapGet("/login", LoginAsync)
            .AllowAnonymous()
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .WithSummary("Start sign-in at Keycloak, then return to a local path.")
            .Produces(StatusCodes.Status302Found)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        bff.MapGet("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithSummary("End the session here and at Keycloak.")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        bff.MapGet("/user", GetUser)
            .WithSummary("The signed-in user, for the SPA's auth gate.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        return app;
    }

    private static async Task<ChallengeHttpResult> LoginAsync([AsParameters] LoginRequest request, RedisTicketStore sessions)
    {
        await sessions.EnsureAvailableAsync();
        AuthenticationProperties properties = new() { RedirectUri = request.ReturnUrl ?? Home };
        return TypedResults.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    // GET with a side effect is safe here: the sid is unguessable to a cross-site page, so it acts as the CSRF token.
    private static async Task<IResult> LogoutAsync(string? sid, HttpContext httpContext)
    {
        AuthenticateResult session = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (session is not { Succeeded: true, Principal: { } user })
        {
            return TypedResults.Redirect(Home);
        }

        string? sessionId = user.FindFirstValue(ClaimNames.SessionId);
        if (sessionId is null || !string.Equals(sid, sessionId, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                detail: "The logout link does not belong to this session.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_logout_request" });
        }

        // Cookie first (deletes the Redis ticket), then OIDC (redirects to Keycloak's end-session with id_token_hint).
        return TypedResults.SignOut(
            new AuthenticationProperties { RedirectUri = Home },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static Ok<BffUserResponse> GetUser(ClaimsPrincipal user) =>
        TypedResults.Ok(new BffUserResponse(
            user.Identity?.Name ?? SessionPrincipal.Subject(user),
            SessionPrincipal.RolesOf(user),
            LogoutPath + "?sid=" + Uri.EscapeDataString(user.FindFirstValue(ClaimNames.SessionId) ?? string.Empty)));
}
