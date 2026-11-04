namespace DisputePortal.Api.Shared.Errors;

/// <summary>Completes every Problem Details body (RFC 9457). Status-code pages and framework rejections arrive with no code or detail.</summary>
public static class ErrorCodes
{
    public const string MalformedRequestDetail = "The request could not be read.";
    public const string UnsupportedMediaTypeDetail = "The request body must be JSON.";
    public const string InternalErrorDetail = "An unexpected error occurred.";

    /// <summary>Fills only what is missing, so a handler's own code and detail always win.</summary>
    public static void Complete(ProblemDetailsContext context)
    {
        int? status = context.ProblemDetails.Status;
        context.ProblemDetails.Extensions.TryAdd("code", ForStatus(status));
        context.ProblemDetails.Detail ??= DetailForStatus(status);
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
    }

    public static string ForStatus(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => "malformed_request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
        StatusCodes.Status409Conflict => "conflict",
        StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
        StatusCodes.Status422UnprocessableEntity => "rule_violation",
        StatusCodes.Status429TooManyRequests => "too_many_requests",
        StatusCodes.Status503ServiceUnavailable => "service_unavailable",
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
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaTypeDetail,
        StatusCodes.Status422UnprocessableEntity => "A business rule refused the request.",
        StatusCodes.Status429TooManyRequests => "Too many requests; try again later.",
        StatusCodes.Status503ServiceUnavailable => "The service is unavailable; try again later.",
        _ => InternalErrorDetail,
    };
}
