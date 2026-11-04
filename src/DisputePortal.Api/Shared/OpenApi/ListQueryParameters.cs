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
}
