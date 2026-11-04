using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Paging;

namespace DisputePortal.Api.Features.Agent;

/// <summary>The back office's reads: the work queue, and a detail whose every successful view is audited.</summary>
public sealed class AgentQueries(
    IAgentDisputeRepository disputes,
    AgentCursorProtector cursors,
    SecurityMetrics securityMetrics,
    ILogger<AgentQueries> logger)
{
    /// <summary>The audit event for an agent opening a customer's dispute: ids only, never the description or notes.</summary>
    public static readonly EventId DisputeViewed = new(5101, "agent.dispute.viewed");

    public async Task<AgentQueueResponse> QueueAsync(AgentQueueRequest request, string agentSubject, CancellationToken cancellationToken)
    {
        AgentQueueFilter filter = AgentQueueFilter.Parse(request);
        DisputeKey? after = string.IsNullOrEmpty(request.Cursor) ? null : cursors.Unprotect(request.Cursor, agentSubject, filter);
        int limit = PageLimit.ParseOrDefault(request.Limit);
        // One extra row says whether a next page exists, without a count query.
        IReadOnlyList<AgentQueueItem> rows = await disputes.ListAsync(filter, after, limit + 1, cancellationToken);
        string? nextCursor = rows.Count > limit
            ? cursors.Protect(new DisputeKey(rows[limit - 1].CreatedAt, rows[limit - 1].Id), agentSubject, filter)
            : null;
        return new AgentQueueResponse([.. rows.Take(limit)], nextCursor);
    }

    /// <exception cref="Shared.Errors.NotFoundException">No dispute has this id.</exception>
    public async Task<VersionedAgentDispute> GetAsync(Guid id, string agentSubject, string traceId, CancellationToken cancellationToken)
    {
        VersionedAgentDispute dispute = await disputes.GetDetailAsync(id, cancellationToken) ?? throw DisputeCommands.DisputeNotFound();
        securityMetrics.Audit(SecurityMetrics.AgentDisputeViewed);
        logger.LogInformation(DisputeViewed, "agent.dispute.viewed {DisputeId} by {AgentSubject} trace {TraceId}", id, agentSubject, traceId);
        return dispute;
    }
}
