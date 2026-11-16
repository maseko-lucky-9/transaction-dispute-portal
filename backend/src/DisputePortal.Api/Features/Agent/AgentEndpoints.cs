using System.Diagnostics;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.OpenApi;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Api.Shared.Validation;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Agent;

public static class AgentEndpoints
{
    public const string BasePath = "/api/v1/agent/disputes";

    private const string IfMatchDescription = "The ETag of the version the agent decided on. Missing: 428; stale, weak or *: 412.";

    /// <summary>Every route here requires the DisputeAgent policy: a customer gets 403, a dual-role session 403 role_conflict.</summary>
    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        RouteGroupBuilder agent = app.MapGroup(BasePath)
            .WithTags("Agent")
            .RequireAuthorization(Policies.DisputeAgent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        agent.MapGet("/", QueueAsync)
            .AddEndpointFilter<ValidationFilter<AgentQueueRequest>>()
            .AddOpenApiOperationTransformer(ListQueryParameters.Describe<DisputeStatus, AgentQueueAssignment>)
            .WithName("ListAgentQueue")
            .WithSummary("The work queue across customers, oldest first. No status or assignment means the open queue (Submitted and UnderReview). assignment (unclaimed, mine or others, relative to the caller) cannot be combined with status.")
            .ProducesValidationProblem();
        agent.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAgentDispute")
            .WithSummary("A dispute with the customer's details and who acted on it; the ETag header is its version. Every view is audited.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer(DisputeOpenApi.ETagOn(StatusCodes.Status200OK));
        agent.MapGet("/{id:guid}/customer-transactions", CustomerTransactionsAsync)
            .AddEndpointFilter<ValidationFilter<AgentCustomerTransactionsRequest>>()
            .AddOpenApiOperationTransformer(ListQueryParameters.Describe<DisputeStatus>)
            .WithName("ListAgentCustomerTransactions")
            .WithSummary("The recent transactions of the customer behind a dispute, newest first, read-only: from the earlier of 90 days ago and the disputed transaction's day. Only while the dispute is Submitted or UnderReview (otherwise 404). Every page read is audited.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        agent.MapPost("/take-next", TakeNextAsync)
            .WithName("TakeNextDispute")
            .WithSummary("Claim the oldest unclaimed (Submitted) dispute nobody else is taking at this moment and start its review. 200 with the dispute and its ETag; 204 when there is none to take.")
            .AddOpenApiOperationTransformer(DisputeOpenApi.ETagOn(StatusCodes.Status200OK));
        agent.MapPost("/{id:guid}/transitions", TransitionAsync)
            .AddEndpointFilter<ValidationFilter<TransitionRequest>>()
            .WithName("TransitionDispute")
            .WithSummary("Move a dispute: Submitted to UnderReview (claim it), UnderReview to Accepted or Rejected (with a note) or back to Submitted (release it, no note). Only the reviewing agent may accept, reject or release (otherwise 409 not_assignee). Requires If-Match.")
            .Produces<AgentDisputeDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .AddOpenApiOperationTransformer(DisputeOpenApi.ETagOn(StatusCodes.Status200OK))
            .AddOpenApiOperationTransformer(DisputeOpenApi.StaleETagOn412)
            .AddOpenApiOperationTransformer(DisputeOpenApi.RequireIfMatch(IfMatchDescription));
        return app;
    }

    private static async Task<Ok<AgentQueueResponse>> QueueAsync(
        [AsParameters] AgentQueueRequest request, AgentQueries queries, HttpContext httpContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.QueueAsync(request, SessionPrincipal.Subject(httpContext.User), TraceId(httpContext), cancellationToken));

    private static async Task<Ok<AgentDisputeDetail>> GetAsync(Guid id, AgentQueries queries, HttpContext httpContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(WithETag(httpContext, await queries.GetAsync(id, SessionPrincipal.Subject(httpContext.User), TraceId(httpContext), cancellationToken)));

    private static async Task<Ok<AgentCustomerTransactionsResponse>> CustomerTransactionsAsync(
        Guid id,
        [AsParameters] AgentCustomerTransactionsRequest request,
        AgentCustomerTransactionQueries queries,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ListAsync(id, request, SessionPrincipal.Subject(httpContext.User), TraceId(httpContext), cancellationToken));

    // Internal so a unit test can run the handler itself for the 204 (a shared test database is never empty).
    internal static async Task<Results<Ok<AgentDisputeDetail>, NoContent>> TakeNextAsync(AgentCommands commands, HttpContext httpContext, CancellationToken cancellationToken) =>
        await commands.TakeNextAsync(SessionPrincipal.Subject(httpContext.User), cancellationToken) is { } claimed
            ? TypedResults.Ok(WithETag(httpContext, claimed))
            : TypedResults.NoContent();

    private static string TraceId(HttpContext httpContext) => Activity.Current?.Id ?? httpContext.TraceIdentifier;

    private static async Task<IResult> TransitionAsync(
        Guid id,
        TransitionRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        AgentCommands commands,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(WithETag(httpContext, await commands.TransitionAsync(id, request, ifMatch, SessionPrincipal.Subject(httpContext.User), cancellationToken)));
        }
        catch (PreconditionFailedException stale)
        {
            return DisputeETag.Stale(httpContext, stale);
        }
    }

    private static AgentDisputeDetail WithETag(HttpContext httpContext, VersionedAgentDispute dispute)
    {
        httpContext.Response.Headers.ETag = DisputeETag.For(dispute.Version);
        return dispute.Detail;
    }
}
