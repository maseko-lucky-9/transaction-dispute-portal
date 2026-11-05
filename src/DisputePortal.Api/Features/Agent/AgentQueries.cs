using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Paging;

namespace DisputePortal.Api.Features.Agent;

/// <summary>The back office's reads: the work queue and a dispute's detail, every successful one audited.</summary>
public sealed class AgentQueries(
    IAgentDisputeRepository disputes,
    AgentCursorProtector cursors,
    SecurityMetrics securityMetrics,
    ILogger<AgentQueries> logger)
{
    /// <summary>The audit event for an agent opening a customer's dispute: ids only, never the description or notes.</summary>
    public static readonly EventId DisputeViewed = new(5101, "agent.dispute.viewed");

    /// <summary>The audit event for an agent loading a page of the queue: ids, the filter and counts, never a row's content.</summary>
    public static readonly EventId QueueViewed = new(5103, "agent.queue.viewed");

    /// <summary>
    /// One page of the queue. A page that was read is audited once, whatever number of rows it holds (an empty page too:
    /// the agent still asked); a refused request (bad filter or cursor) read nothing and is not.
    /// </summary>
    public async Task<AgentQueueResponse> QueueAsync(AgentQueueRequest request, string agentSubject, string traceId, CancellationToken cancellationToken)
    {
        AgentQueueFilter filter = AgentQueueFilter.Parse(request);
        DisputeKey? after = string.IsNullOrEmpty(request.Cursor) ? null : cursors.Unprotect(request.Cursor, agentSubject, filter);
        int limit = PageLimit.ParseOrDefault(request.Limit);
        // One extra row says whether a next page exists, without a count query.
        IReadOnlyList<AgentQueueItem> rows = await disputes.ListAsync(filter, after, limit + 1, cancellationToken);
        string? nextCursor = rows.Count > limit
            ? cursors.Protect(new DisputeKey(rows[limit - 1].CreatedAt, rows[limit - 1].Id), agentSubject, filter)
            : null;
        AgentQueueResponse page = new([.. rows.Take(limit)], nextCursor);
        AuditQueueViewed(filter, limit, page.Items.Count, agentSubject, traceId);
        return page;
    }

    /// <exception cref="Shared.Errors.NotFoundException">No dispute has this id.</exception>
    public async Task<VersionedAgentDispute> GetAsync(Guid id, string agentSubject, string traceId, CancellationToken cancellationToken)
    {
        VersionedAgentDispute dispute = await disputes.GetDetailAsync(id, cancellationToken) ?? throw DisputeNotFound.ToAgent();
        securityMetrics.Audit(SecurityMetrics.AgentDisputeViewed);
        logger.LogInformation(DisputeViewed, "agent.dispute.viewed {DisputeId} by {AgentSubject} trace {TraceId}", id, agentSubject, traceId);
        return dispute;
    }

    private void AuditQueueViewed(AgentQueueFilter filter, int limit, int returned, string agentSubject, string traceId)
    {
        securityMetrics.Audit(SecurityMetrics.AgentQueueViewed);
        logger.LogInformation(
            QueueViewed,
            "agent.queue.viewed filter {Filter} limit {Limit} returned {Returned} by {AgentSubject} trace {TraceId}",
            filter.Label,
            limit,
            returned,
            agentSubject,
            traceId);
    }
}
