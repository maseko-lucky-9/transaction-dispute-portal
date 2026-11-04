using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>
/// The HTTP headers of the dispute contract, which the generator cannot infer from the handlers: ETag on every dispute
/// representation and on 412, Location on 201, and If-Match on withdraw (required; the server still answers 428 without it).
/// </summary>
public static class DisputeOpenApi
{
    private const string ETagDescription = "The dispute's version (strong, quoted). Send it back as If-Match to change the dispute.";
    private const string StaleETagDescription = "The current version, when the dispute still exists: reload, then retry with it.";

    public static Func<OpenApiOperation, OpenApiOperationTransformerContext, CancellationToken, Task> ETagOn(params int[] statuses) =>
        (operation, _, _) =>
        {
            foreach (int status in statuses)
            {
                AddHeader(operation, status, "ETag", ETagDescription, required: true);
            }

            return Task.CompletedTask;
        };

    public static Task StaleETagOn412(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        AddHeader(operation, StatusCodes.Status412PreconditionFailed, "ETag", StaleETagDescription, required: false);
        return Task.CompletedTask;
    }

    public static Task LocationOn201(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        AddHeader(operation, StatusCodes.Status201Created, "Location", "The new dispute: /api/v1/disputes/{id}.", required: true);
        return Task.CompletedTask;
    }

    public static Task RequireIfMatch(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        OpenApiParameter ifMatch = operation.Parameters?.OfType<OpenApiParameter>().SingleOrDefault(parameter => parameter.Name == "If-Match")
            ?? throw new InvalidOperationException("The withdraw operation has no If-Match parameter to describe.");
        ifMatch.Required = true;
        ifMatch.Description = "The ETag of the version being withdrawn. Missing: 428; stale, weak or *: 412.";
        return Task.CompletedTask;
    }

    // A missing response is a wiring defect (the endpoint lost its Produces metadata), so it fails loudly.
    private static void AddHeader(OpenApiOperation operation, int status, string name, string description, bool required)
    {
        string key = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (operation.Responses?.GetValueOrDefault(key) is not OpenApiResponse response)
        {
            throw new InvalidOperationException($"Operation {operation.OperationId} documents no {key} response for header {name}.");
        }

        response.Headers ??= new Dictionary<string, IOpenApiHeader>();
        response.Headers[name] = new OpenApiHeader
        {
            Description = description,
            Required = required,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };
    }
}
