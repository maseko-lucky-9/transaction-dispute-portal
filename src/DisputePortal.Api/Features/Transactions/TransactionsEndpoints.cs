using System.Globalization;
using System.Text.Json.Nodes;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Api.Shared.Validation;
using DisputePortal.Domain.Transactions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Features.Transactions;

public static class TransactionsEndpoints
{
    public static WebApplication MapTransactionsEndpoints(this WebApplication app)
    {
        RouteGroupBuilder transactions = app.MapGroup("/api/v1/transactions").WithTags("Transactions").RequireCustomer();
        transactions.MapGet("/", ListAsync)
            .AddEndpointFilter<ValidationFilter<TransactionListRequest>>()
            .WithName("ListTransactions")
            .WithSummary("My transactions, newest first, keyset-paged.")
            .ProducesValidationProblem()
            .AddOpenApiOperationTransformer(DescribeListParameters);
        // The guid constraint turns a malformed id into a 404, like an absent one.
        transactions.MapGet("/{id:guid}", GetAsync)
            .WithName("GetTransaction")
            .WithSummary("One of my transactions; another customer's id is 404.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    /// <summary>
    /// The query binds as strings so a bad value is a 400 naming its field (not a binding failure); the document states
    /// the real types, which the web client's generated types rely on.
    /// </summary>
    private static Task DescribeListParameters(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
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
                    Maximum = TransactionListRequestValidator.MaxLimit.ToString(CultureInfo.InvariantCulture),
                    Default = TransactionQueries.DefaultLimit,
                },
                "from" or "to" => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
                "status" => new OpenApiSchema { Type = JsonSchemaType.String, Enum = [.. Enum.GetNames<TransactionStatus>().Select(name => (JsonNode)name)] },
                "cursor" => new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = TransactionListRequestValidator.MaxCursorLength },
                _ => parameter.Schema,
            };
        }

        return Task.CompletedTask;
    }

    private static async Task<Ok<TransactionListResponse>> ListAsync(
        [AsParameters] TransactionListRequest request, TransactionQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ListAsync(request, cancellationToken));

    private static async Task<Ok<TransactionItem>> GetAsync(Guid id, TransactionQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.GetAsync(id, cancellationToken));
}
