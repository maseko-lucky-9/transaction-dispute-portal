using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// An agent's move on a dispute, in the same order as the customer's withdraw (ADR-007): existence (404), If-Match
/// (428 missing, 412 stale, which wins over an illegal move), the lifecycle table (409 invalid_transition, so an agent
/// asking for Withdrawn is 409 like any other move the table does not hold), then who may make it (409 not_assignee:
/// only the reviewing agent accepts, rejects or releases, ADR-019; each refusal is logged and counted), then the write,
/// where a change landing in between fails the xmin check and is a 412 with the current ETag. Logs carry ids and codes only.
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

    public static readonly EventId DisputeDenied = new(5105, "agent.dispute.denied");

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
        TransitionAsAssignee(dispute, to, agentSubject, request.Note, now);
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
        return await disputes.GetDetailAsync(id, agentSubject, cancellationToken) ?? throw DisputeNotFound.ToAgent();
    }

    /// <summary>
    /// Take next (ADR-019): claims the oldest Submitted dispute that no concurrent take-next holds, in one transaction
    /// (lock, move, save, read the detail, commit), so two agents never get the same one. Null when there is none to take.
    /// The move goes through the aggregate like any other. The detail is read before the commit, while the row lock is
    /// still held, so what is returned is the claim itself: a start review or a customer withdrawal racing it waits on
    /// the lock, then fails the xmin check (412), and cannot land between the claim and the answer. Its xmin, the ETag,
    /// is this transaction's and does not change on commit. Known limit: if the commit lands but its acknowledgement is
    /// lost, the retried attempt claims a second dispute; the first stays under review by this agent (their "mine"), who
    /// can release it.
    /// </summary>
    public async Task<VersionedAgentDispute?> TakeNextAsync(string agentSubject, CancellationToken cancellationToken)
    {
        VersionedAgentDispute? claimed = await unitOfWork.SaveInTransactionAsync(
            token => ClaimOldestAsync(agentSubject, token),
            (id, token) => id is { } claimedId ? ReadClaimAsync(claimedId, agentSubject, token) : Task.FromResult<VersionedAgentDispute?>(null),
            cancellationToken);
        if (claimed is null)
        {
            return null;
        }

        disputeMetrics.Transitioned(DisputeStatus.UnderReview);
        securityMetrics.Audit(SecurityMetrics.AgentDisputeTransitioned);
        logger.LogInformation(
            DisputeTransitioned, "agent.dispute.transitioned {DisputeId} {From} to {To} by {AgentSubject}", claimed.Detail.Id, DisputeStatus.Submitted, DisputeStatus.UnderReview, agentSubject);
        return claimed;
    }

    // One attempt, run again whole on a transient failure: the lock is taken again, so a retry may pick another row.
    private async Task<Guid?> ClaimOldestAsync(string agentSubject, CancellationToken cancellationToken)
    {
        Dispute? dispute = await disputes.LockOldestUnclaimedAsync(cancellationToken);
        if (dispute is null)
        {
            return null;
        }

        dispute.Transition(DisputeStatus.UnderReview, Actor.Agent(agentSubject), note: null, time.GetUtcNow());
        return dispute.Id;
    }

    // Inside the transaction: a claim that cannot be read back rolls back with it instead of being left behind.
    private async Task<VersionedAgentDispute?> ReadClaimAsync(Guid id, string agentSubject, CancellationToken cancellationToken) =>
        await disputes.GetDetailAsync(id, agentSubject, cancellationToken) ?? throw DisputeNotFound.ToAgent();

    // A refusal because another agent is reviewing: counted and logged with the dispute and the refused agent only, never
    // the reviewing agent, so the log does not become a way to learn who holds what.
    private void TransitionAsAssignee(Dispute dispute, DisputeStatus to, string agentSubject, string? note, DateTimeOffset now)
    {
        try
        {
            dispute.Transition(to, Actor.Agent(agentSubject), note, now);
        }
        catch (NotAssigneeException)
        {
            securityMetrics.Audit(SecurityMetrics.AgentDisputeDenied);
            logger.LogInformation(DisputeDenied, "agent.dispute.denied {DisputeId} by {AgentSubject}", dispute.Id, agentSubject);
            throw;
        }
    }

    // A status name, in any case, and nothing else: TryParse alone also takes "1", "99" (undefined, and its own name) and "Accepted,Rejected".
    private static DisputeStatus ParseTarget(string? toStatus) =>
        Enum.TryParse(toStatus, ignoreCase: true, out DisputeStatus status)
            && Enum.IsDefined(status)
            && string.Equals(status.ToString(), toStatus, StringComparison.OrdinalIgnoreCase)
            ? status
            : throw new UnknownTargetStatusException();
}
