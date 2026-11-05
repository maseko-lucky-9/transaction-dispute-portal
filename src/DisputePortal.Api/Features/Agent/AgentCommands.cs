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
/// The target status is read first: over HTTP the validator has already answered a bad one with 400, and the command
/// does not rely on that. An agent's 404 is never counted as bola_404 (see <see cref="DisputeNotFound.ToAgent"/>).
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

    /// <exception cref="UnknownTargetStatusException">The request names no status (the validator answers this first over HTTP).</exception>
    public async Task<VersionedAgentDispute> TransitionAsync(
        Guid id, TransitionRequest request, string? ifMatch, string agentSubject, CancellationToken cancellationToken)
    {
        DisputeStatus to = ParseTarget(request.ToStatus);
        Dispute dispute = await disputes.FindAsync(id, cancellationToken) ?? throw DisputeNotFound.ToAgent();
        if (ifMatch is null)
        {
            throw new PreconditionRequiredException();
        }

        if (!DisputeETag.Matches(ifMatch, dispute.Version))
        {
            throw new PreconditionFailedException(DisputeETag.For(dispute.Version));
        }

        DisputeStatus from = dispute.Status;
        DateTimeOffset now = time.GetUtcNow();
        dispute.Transition(to, Actor.Agent(agentSubject), request.Note, now);
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
        if (DisputeLifecycle.IsResolution(to))
        {
            // After the commit, so a refused or lost write records nothing; `now` is the instant stored as resolved_at.
            disputeMetrics.Resolved(to, now - dispute.CreatedAt);
        }

        securityMetrics.Audit(SecurityMetrics.AgentDisputeTransitioned);
        logger.LogInformation(DisputeTransitioned, "agent.dispute.transitioned {DisputeId} {From} to {To} by {AgentSubject}", id, from, to, agentSubject);
        return await disputes.GetDetailAsync(id, cancellationToken) ?? throw DisputeNotFound.ToAgent();
    }

    // A status name, in any case, and nothing else: TryParse alone also takes "1", "99" (undefined, and its own name) and "Accepted,Rejected".
    private static DisputeStatus ParseTarget(string? toStatus) =>
        Enum.TryParse(toStatus, ignoreCase: true, out DisputeStatus status)
            && Enum.IsDefined(status)
            && string.Equals(status.ToString(), toStatus, StringComparison.OrdinalIgnoreCase)
            ? status
            : throw new UnknownTargetStatusException();
}
