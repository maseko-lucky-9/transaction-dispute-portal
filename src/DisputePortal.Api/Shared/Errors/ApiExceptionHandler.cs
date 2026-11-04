using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DisputePortal.Api.Shared.Errors;

/// <summary>The one place an exception becomes an HTTP response (SCAD ADR-004).</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem = Describe(exception);
        int status = problem.Status!.Value;
        string code = (string)problem.Extensions["code"]!;
        if (status >= StatusCodes.Status500InternalServerError)
        {
            // .NET 10 no longer logs exceptions a handler has handled, so 5xx are logged here.
            logger.LogError(exception, "Request failed with {Status} {Code}", status, code);
        }

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "1";
        }

        await ProblemResponses.WriteAsync(httpContext, problem);
        return true;
    }

    // No stack trace or exception text ever reaches the client.
    private static ProblemDetails Describe(Exception exception) => exception switch
    {
        ApiException api => api.ToProblem(),
        // A query the database refused as data (a save is translated by the unit of work): 400, not 500.
        PostgresException postgres when DbErrorTranslator.Translate(postgres) is { } refusal => refusal.ToProblem(),
        // An illegal move conflicts with the dispute's current state (ADR-007): 409, not a rule violation.
        InvalidTransitionException transition => Problem(StatusCodes.Status409Conflict, transition.Code, transition.Message),
        DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message),
        RetryLimitExceededException or NpgsqlException { IsTransient: true } =>
            Problem(StatusCodes.Status503ServiceUnavailable, "database_unavailable", ErrorCodes.DetailForStatus(StatusCodes.Status503ServiceUnavailable)),
        BadHttpRequestException { StatusCode: StatusCodes.Status415UnsupportedMediaType } =>
            Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type", ErrorCodes.UnsupportedMediaTypeDetail),
        BadHttpRequestException bad => Problem(bad.StatusCode, "malformed_request", ErrorCodes.MalformedRequestDetail),
        _ => Problem(StatusCodes.Status500InternalServerError, "internal_error", ErrorCodes.InternalErrorDetail),
    };

    private static ProblemDetails Problem(int status, string code, string detail) =>
        new() { Status = status, Detail = detail, Extensions = { ["code"] = code } };
}
