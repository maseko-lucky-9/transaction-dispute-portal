using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Health;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DisputePortal.Api.Features.Bff;

/// <summary>
/// POST /bff/backchannel-logout (OpenID Connect Back-Channel Logout 1.0). Keycloak calls it over the internal data network
/// when a session ends there (admin, idle or max timeout, logout elsewhere); every ticket signed in under that Keycloak
/// session is deleted at once, instead of at the next refresh. Anonymous and CSRF-exempt because the signed token is the
/// authentication, and served only on the management port, which is never published (ADR-011). Not in the OpenAPI
/// document: it is Keycloak's contract, not the SPA's.
/// </summary>
public static class BackchannelLogoutEndpoint
{
    /// <summary>The full path; mapped as <see cref="RouteInGroup"/> under the /bff group.</summary>
    public const string Path = "/bff" + RouteInGroup;
    public const string RefusedCode = "invalid_logout_token";
    public const string RefusedDetail = "The logout token was refused.";

    /// <summary>A logout token plus its field name, with room to spare; the form must declare its length.</summary>
    public const long MaxBodyBytes = LogoutTokenValidator.MaxTokenLength + 1024;

    private const string RouteInGroup = "/backchannel-logout";
    private const string TokenField = "logout_token";
    private const string FormMediaType = "application/x-www-form-urlencoded";
    private const string LogCategory = "DisputePortal.Api.Features.Bff.BackchannelLogout";

    private static readonly FormOptions FormLimits = new()
    {
        ValueCountLimit = 4,
        KeyLengthLimit = 64,
        ValueLengthLimit = LogoutTokenValidator.MaxTokenLength,
        BufferBody = false,
    };

    public static RouteGroupBuilder MapBackchannelLogout(this RouteGroupBuilder bff)
    {
        bff.MapPost(RouteInGroup, HandleAsync)
            .AllowAnonymous()
            .ServeOnManagementPortOnly()
            .RequireRateLimiting(RateLimits.BackchannelLogoutPolicy)
            .ExcludeFromDescription();
        return bff;
    }

    // Every refusal, whatever the rule, is the same 400 with the same body (spec section 2.8): no oracle for a forger.
    // A valid token for a session this API never saw (or already ended) is a 200, as the spec requires.
    private static async Task<IResult> HandleAsync(
        HttpContext context, LogoutTokenValidator validator, RedisTicketStore sessions, SecurityMetrics metrics, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        ILogger logger = loggers.CreateLogger(LogCategory);
        LogoutTokenResult result = await validator.ValidateAsync(await ReadTokenAsync(context.Request, cancellationToken), cancellationToken);
        if (!result.IsAccepted)
        {
            metrics.Record(SecurityMetrics.BackchannelLogoutRejected);
            logger.LogWarning("Back-channel logout token refused: {Reason}", result.Failure);
            return ProblemResponses.Result(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = RefusedDetail,
                Extensions = { ["code"] = RefusedCode },
            });
        }

        int ended = await sessions.RemoveBySessionIdAsync(result.SessionId!);
        metrics.Record(SecurityMetrics.BackchannelLogout);
        // A count only: no sid, sub or jti ever reaches a log line.
        logger.LogInformation("Back-channel logout ended {SessionCount} session(s)", ended);
        return TypedResults.Ok();
    }

    /// <returns>The one <c>logout_token</c> of a small urlencoded form with a declared length; null for anything else.</returns>
    private static async Task<string?> ReadTokenAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!IsUrlEncodedForm(request.ContentType) || request.ContentLength is not (> 0 and <= MaxBodyBytes))
        {
            return null;
        }

        try
        {
            IFormCollection form = await request.ReadFormAsync(FormLimits, cancellationToken);
            return form[TokenField] is { Count: 1 } token ? token.ToString() : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            return null;
        }
    }

    private static bool IsUrlEncodedForm(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mediaType)
        && mediaType.MediaType.Equals(FormMediaType, StringComparison.OrdinalIgnoreCase);
}
