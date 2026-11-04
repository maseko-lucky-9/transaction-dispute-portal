using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.OpenApi;
using DisputePortal.Api.Shared.Security;
using DisputePortal.Api.Shared.Validation;
using DisputePortal.Domain.Disputes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Disputes;

public static class DisputesEndpoints
{
    public const string BasePath = "/api/v1/disputes";

    public static WebApplication MapDisputesEndpoints(this WebApplication app)
    {
        app.MapGroup("/api/v1/dispute-reasons").WithTags("Disputes").RequireCustomer()
            .MapGet("/", ReasonsAsync)
            .WithName("ListDisputeReasons")
            .WithSummary("The reasons a dispute can give, in display order.");

        RouteGroupBuilder disputes = app.MapGroup(BasePath).WithTags("Disputes").RequireCustomer();
        disputes.MapPost("/", SubmitAsync)
            .AddEndpointFilter<ValidationFilter<CreateDisputeRequest>>()
            .RequireRateLimiting(RateLimits.DisputeSubmitPolicy)
            .WithName("SubmitDispute")
            .WithSummary("Dispute one of my transactions. Idempotent on the client-generated id: a retry returns 200 with the original.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .Produces<DisputeConflictProblem>(StatusCodes.Status409Conflict, ProblemResponses.ContentType)
            .Produces<EligibilityProblem>(StatusCodes.Status422UnprocessableEntity, ProblemResponses.ContentType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        disputes.MapGet("/", ListAsync)
            .AddEndpointFilter<ValidationFilter<DisputeListRequest>>()
            .AddOpenApiOperationTransformer(ListQueryParameters.Describe<DisputeStatus>)
            .WithName("ListDisputes")
            .WithSummary("My disputes in every status, newest first, keyset-paged.")
            .ProducesValidationProblem();
        disputes.MapGet("/{id:guid}", GetAsync)
            .WithName("GetDispute")
            .WithSummary("One of my disputes with its timeline; the ETag header is its version. Another customer's id is 404.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        disputes.MapPost("/{id:guid}/withdraw", WithdrawAsync)
            .WithName("WithdrawDispute")
            .WithSummary("Withdraw a Submitted or UnderReview dispute. Requires If-Match with the dispute's ETag.")
            .Produces<DisputeDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        return app;
    }

    private static async Task<Ok<IReadOnlyList<DisputeReason>>> ReasonsAsync(DisputeQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ReasonsAsync(cancellationToken));

    private static async Task<Results<Created<DisputeDetail>, Ok<DisputeDetail>>> SubmitAsync(
        CreateDisputeRequest request, DisputeCommands commands, HttpContext httpContext, CancellationToken cancellationToken)
    {
        SubmitOutcome outcome = await commands.SubmitAsync(request, SessionPrincipal.Subject(httpContext.User), cancellationToken);
        DisputeDetail detail = WithETag(httpContext, outcome.Dispute);
        return outcome.Created ? TypedResults.Created($"{BasePath}/{detail.Id}", detail) : TypedResults.Ok(detail);
    }

    private static async Task<Ok<DisputeListResponse>> ListAsync(
        [AsParameters] DisputeListRequest request, DisputeQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ListAsync(request, cancellationToken));

    private static async Task<Ok<DisputeDetail>> GetAsync(Guid id, DisputeQueries queries, HttpContext httpContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(WithETag(httpContext, await queries.GetAsync(id, cancellationToken)));

    private static async Task<IResult> WithdrawAsync(
        Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, DisputeCommands commands, HttpContext httpContext, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(WithETag(httpContext, await commands.WithdrawAsync(id, ifMatch, SessionPrincipal.Subject(httpContext.User), cancellationToken)));
        }
        catch (PreconditionFailedException stale)
        {
            // Answered here, not by the exception handler, which strips ETag: a 412 must carry the current one.
            if (stale.CurrentETag is not null)
            {
                httpContext.Response.Headers.ETag = stale.CurrentETag;
            }

            return ProblemResponses.Result(stale.ToProblem());
        }
    }

    private static DisputeDetail WithETag(HttpContext httpContext, VersionedDispute dispute)
    {
        httpContext.Response.Headers.ETag = DisputeETag.For(dispute.Version);
        return dispute.Detail;
    }
}
