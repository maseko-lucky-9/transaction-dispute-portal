using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// An agent's move on a dispute, in the same order as the customer's withdraw (ADR-007): existence (404), If-Match
/// (428 missing, 412 stale, which wins over an illegal move), the lifecycle table (409 invalid_transition, so an agent
/// asking for Withdrawn or Submitted is 409 like any other move the table does not hold), then the write, where a
/// change landing in between fails the xmin check and is a 412 with the current ETag. Logs carry ids and codes only.
/// </summary>
public sealed class AgentCommands(
    IAgentDisputeRepository disputes,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    DisputeMetrics disputeMetrics,
    SecurityMetrics securityMetrics,
    ILogger<AgentCommands> logger)
{
    public static readonly EventId DisputeTransitioned = new(5102, "agent.dispute.transitioned");

    public async Task<VersionedAgentDispute> TransitionAsync(
        Guid id, TransitionRequest request, string? ifMatch, string agentSubject, CancellationToken cancellationToken)
    {
        Dispute dispute = await disputes.FindAsync(id, cancellationToken) ?? throw DisputeCommands.DisputeNotFound();
        if (ifMatch is null)
        {
            throw new PreconditionRequiredException();
        }

        if (!DisputeETag.Matches(ifMatch, dispute.Version))
        {
            throw new PreconditionFailedException(DisputeETag.For(dispute.Version));
        }

        DisputeStatus from = dispute.Status;
        DisputeStatus to = Enum.Parse<DisputeStatus>(request.ToStatus!, ignoreCase: true);
        dispute.Transition(to, Actor.Agent(agentSubject), request.Note, time.GetUtcNow());
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException conflict)
        {
            uint? current = await disputes.GetVersionAsync(id, cancellationToken);
            throw new PreconditionFailedException(current is { } version ? DisputeETag.For(version) : null, conflict);
        }

        disputeMetrics.Transitioned(to);
        securityMetrics.Audit(SecurityMetrics.AgentDisputeTransitioned);
        logger.LogInformation(DisputeTransitioned, "agent.dispute.transitioned {DisputeId} {From} to {To} by {AgentSubject}", id, from, to, agentSubject);
        return await disputes.GetDetailAsync(id, cancellationToken) ?? throw DisputeCommands.DisputeNotFound();
    }
}
