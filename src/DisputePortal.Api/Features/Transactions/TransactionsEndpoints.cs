using DisputePortal.Api.Shared.Security;
using DisputePortal.Api.Shared.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

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
            .ProducesValidationProblem();
        // The guid constraint turns a malformed id into a 404, like an absent one.
        transactions.MapGet("/{id:guid}", GetAsync)
            .WithName("GetTransaction")
            .WithSummary("One of my transactions; another customer's id is 404.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Ok<TransactionListResponse>> ListAsync(
        [AsParameters] TransactionListRequest request, TransactionQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ListAsync(request, cancellationToken));

    private static async Task<Ok<TransactionItem>> GetAsync(Guid id, TransactionQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.GetAsync(id, cancellationToken));
}
