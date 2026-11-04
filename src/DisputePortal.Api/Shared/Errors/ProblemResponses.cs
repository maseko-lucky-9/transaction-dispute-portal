using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Shared.Errors;

/// <summary>Writes an RFC 9457 body from middleware and auth events, through the same service the exception handler uses.</summary>
public static class ProblemResponses
{
    public static async Task WriteAsync(HttpContext httpContext, int status, string code, string? detail = null)
    {
        httpContext.Response.StatusCode = status;
        IProblemDetailsService problemDetails = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Detail = detail ?? ErrorCodes.DetailForStatus(status),
                Extensions = { ["code"] = code },
            },
        });
    }
}
