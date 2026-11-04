using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace DisputePortal.Api.Shared.Errors;

/// <summary>Completes every Problem Details body (RFC 9457): type, title, detail, instance, code and traceId.</summary>
public static class ErrorCodes
{
    public const string MalformedRequestDetail = "The request could not be read.";
    public const string UnsupportedMediaTypeDetail = "The request body must be JSON.";
    public const string InternalErrorDetail = "An unexpected error occurred.";

    private const string Rfc9110 = "https://tools.ietf.org/html/rfc9110#section-";
    private const string Rfc6585 = "https://tools.ietf.org/html/rfc6585#section-";

    /// <summary>Fills only what is missing, so a caller's own code and detail always win.</summary>
    public static void Complete(HttpContext httpContext, ProblemDetails problem)
    {
        int status = problem.Status ??= httpContext.Response.StatusCode;
        problem.Type ??= TypeForStatus(status);
        problem.Title ??= ReasonPhrases.GetReasonPhrase(status);
        problem.Detail ??= DetailForStatus(status);
        problem.Instance ??= httpContext.Request.Path;
        problem.Extensions.TryAdd("code", ForStatus(status));
        problem.Extensions.TryAdd("traceId", Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }

    /// <summary>For the framework's own problem writer (AddProblemDetails), should any framework path still use it.</summary>
    public static void Complete(ProblemDetailsContext context) => Complete(context.HttpContext, context.ProblemDetails);

    public static string ForStatus(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => "malformed_request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
        StatusCodes.Status409Conflict => "conflict",
        StatusCodes.Status413PayloadTooLarge => "payload_too_large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
        StatusCodes.Status422UnprocessableEntity => "rule_violation",
        StatusCodes.Status429TooManyRequests => "too_many_requests",
        StatusCodes.Status503ServiceUnavailable => "service_unavailable",
        StatusCodes.Status504GatewayTimeout => "timeout",
        _ => "internal_error",
    };

    public static string DetailForStatus(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => MalformedRequestDetail,
        StatusCodes.Status401Unauthorized => "Sign in to continue.",
        StatusCodes.Status403Forbidden => "This request is not allowed.",
        StatusCodes.Status404NotFound => "Nothing exists at this path.",
        StatusCodes.Status405MethodNotAllowed => "This path does not accept this method.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state.",
        StatusCodes.Status413PayloadTooLarge => "The request body is too large.",
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaTypeDetail,
        StatusCodes.Status422UnprocessableEntity => "A business rule refused the request.",
        StatusCodes.Status429TooManyRequests => "Too many requests; try again later.",
        StatusCodes.Status503ServiceUnavailable => "The service is unavailable; try again later.",
        StatusCodes.Status504GatewayTimeout => "The request took too long.",
        _ => InternalErrorDetail,
    };

    // The same references the framework's own problem writer uses.
    public static string TypeForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => Rfc9110 + "15.5.1",
        StatusCodes.Status401Unauthorized => Rfc9110 + "15.5.2",
        StatusCodes.Status403Forbidden => Rfc9110 + "15.5.4",
        StatusCodes.Status404NotFound => Rfc9110 + "15.5.5",
        StatusCodes.Status405MethodNotAllowed => Rfc9110 + "15.5.6",
        StatusCodes.Status409Conflict => Rfc9110 + "15.5.10",
        StatusCodes.Status412PreconditionFailed => Rfc9110 + "15.5.13",
        StatusCodes.Status413PayloadTooLarge => Rfc9110 + "15.5.14",
        StatusCodes.Status415UnsupportedMediaType => Rfc9110 + "15.5.16",
        StatusCodes.Status422UnprocessableEntity => Rfc9110 + "15.5.21",
        StatusCodes.Status428PreconditionRequired => Rfc6585 + "3",
        StatusCodes.Status429TooManyRequests => Rfc6585 + "4",
        StatusCodes.Status500InternalServerError => Rfc9110 + "15.6.1",
        StatusCodes.Status503ServiceUnavailable => Rfc9110 + "15.6.4",
        StatusCodes.Status504GatewayTimeout => Rfc9110 + "15.6.5",
        _ => "about:blank",
    };
}
