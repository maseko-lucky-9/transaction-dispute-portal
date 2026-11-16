using System.Globalization;
using System.Text.Json.Nodes;
using DisputePortal.Api.Shared.Paging;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Shared.OpenApi;

/// <summary>
/// List queries bind as strings so a bad value is a 400 naming its field (not a binding failure); this states the real
/// types in the document, which the web client's generated types rely on.
/// </summary>
public static class ListQueryParameters
{
    public static Task Describe<TStatus>(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        where TStatus : struct, Enum
    {
        foreach (OpenApiParameter parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            parameter.Schema = parameter.Name switch
            {
                "limit" => new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Minimum = "1",
                    Maximum = PageLimit.Max.ToString(CultureInfo.InvariantCulture),
                    Default = PageLimit.Default,
                },
                "from" or "to" => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
                "status" => new OpenApiSchema { Type = JsonSchemaType.String, Enum = [.. Enum.GetNames<TStatus>().Select(name => (JsonNode)name)] },
                "cursor" => new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = PageLimit.MaxCursorLength },
                _ => parameter.Schema,
            };
        }

        return Task.CompletedTask;
    }

    /// <summary>As <see cref="Describe{TStatus}"/>, plus an <c>assignment</c> filter whose values are the lower-case names of <typeparamref name="TAssignment"/> (any case is accepted).</summary>
    public static async Task Describe<TStatus, TAssignment>(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        where TStatus : struct, Enum
        where TAssignment : struct, Enum
    {
        await Describe<TStatus>(operation, context, cancellationToken);
        foreach (OpenApiParameter parameter in operation.Parameters?.OfType<OpenApiParameter>().Where(parameter => parameter.Name == "assignment") ?? [])
        {
            parameter.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [.. Enum.GetNames<TAssignment>().Select(name => (JsonNode)name.ToLowerInvariant())],
            };
        }
    }
}
