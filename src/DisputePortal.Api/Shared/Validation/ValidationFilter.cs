using System.Text.Json;
using FluentValidation;
using DisputePortal.Api.Shared.Errors;
using FluentValidation.Results;

namespace DisputePortal.Api.Shared.Validation;

/// <summary>Runs the request's validator before the handler; failures become 400 validation_failed (SCAD ADR-004).</summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    // Set here: the fallback detail for a 400 describes an unreadable request, which this is not.
    private const string Detail = "One or more fields are invalid.";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Single() throws if the filter is attached to an endpoint without a T: a wiring defect, not a client error.
        T argument = context.Arguments.OfType<T>().Single();
        ValidationResult result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        // Keys match the JSON property names, so the UI can bind each message to its field.
        Dictionary<string, string[]> errors = result.Errors
            .GroupBy(failure => JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());
        return ProblemResponses.Result(new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Detail = Detail,
            Extensions = { ["code"] = "validation_failed" },
        });
    }
}
