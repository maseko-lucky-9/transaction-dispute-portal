using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Shared.Errors;

/// <summary>
/// The one writer of error bodies (RFC 9457). Always <c>application/problem+json</c>, whatever the Accept header says:
/// the framework's content negotiation finds no writer for <c>Accept: text/html</c> and throws, turning a 401 into a 500.
/// Middleware, auth events, the exception handler, status-code pages and endpoint results all come through here.
/// </summary>
public static class ProblemResponses
{
    public const string ContentType = "application/problem+json";

    public static Task WriteAsync(HttpContext httpContext, int status, string code, string? detail = null) =>
        WriteAsync(httpContext, new ProblemDetails { Status = status, Detail = detail, Extensions = { ["code"] = code } });

    public static Task WriteAsync(HttpContext httpContext, ProblemDetails problem)
    {
        ErrorCodes.Complete(httpContext, problem);
        httpContext.Response.StatusCode = problem.Status!.Value;
        // The runtime type, so a validation problem keeps its errors; serializer options come from the app's JSON settings.
        return httpContext.Response.WriteAsJsonAsync(problem, problem.GetType(), options: null, contentType: ContentType);
    }

    /// <summary>An endpoint result that writes through <see cref="WriteAsync(HttpContext, ProblemDetails)"/>.</summary>
    public static IResult Result(ProblemDetails problem) => new ProblemResult(problem);

    private sealed class ProblemResult(ProblemDetails problem) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => WriteAsync(httpContext, problem);
    }
}
