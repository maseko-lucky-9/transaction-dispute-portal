using DisputePortal.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DisputePortal.Api.Shared.Errors;

/// <summary>The one place an exception becomes an HTTP response (SCAD ADR-004).</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string code, string detail) = Describe(exception);
        if (status >= StatusCodes.Status500InternalServerError)
        {
            // .NET 10 no longer logs exceptions a handler has handled, so 5xx are logged here.
            logger.LogError(exception, "Request failed with {Status} {Code}", status, code);
        }

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "1";
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Detail = detail, Extensions = { ["code"] = code } },
        });
    }

    // No stack trace or exception text ever reaches the client.
    private static (int Status, string Code, string Detail) Describe(Exception exception) => exception switch
    {
        ApiException api => (api.Status, api.Code, api.Message),
        DomainException domain => (StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message),
        RetryLimitExceededException or NpgsqlException { IsTransient: true } =>
            (StatusCodes.Status503ServiceUnavailable, "database_unavailable", ErrorCodes.DetailForStatus(StatusCodes.Status503ServiceUnavailable)),
        BadHttpRequestException { StatusCode: StatusCodes.Status415UnsupportedMediaType } =>
            (StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type", ErrorCodes.UnsupportedMediaTypeDetail),
        BadHttpRequestException bad => (bad.StatusCode, "malformed_request", ErrorCodes.MalformedRequestDetail),
        _ => (StatusCodes.Status500InternalServerError, "internal_error", ErrorCodes.InternalErrorDetail),
    };
}
