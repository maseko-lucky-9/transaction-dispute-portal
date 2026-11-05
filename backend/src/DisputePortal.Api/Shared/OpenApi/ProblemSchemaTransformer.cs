using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Shared.OpenApi;

/// <summary>
/// Every problem body carries <c>code</c> and <c>traceId</c> (ErrorCodes.Complete) as extension members, which the
/// generator cannot see; stating them makes <c>code</c>, the contract for client logic, a typed field in the web client.
/// </summary>
public sealed class ProblemSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (!typeof(ProblemDetails).IsAssignableFrom(context.JsonTypeInfo.Type) || schema.Properties is null)
        {
            return Task.CompletedTask;
        }

        schema.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Stable, machine-readable error code." };
        schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Correlation id, as in the logs and traces." };
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        schema.Required.Add("code");
        schema.Required.Add("traceId");
        if (schema.Properties.ContainsKey("violations"))
        {
            schema.Required.Add("violations");
        }

        return Task.CompletedTask;
    }
}
