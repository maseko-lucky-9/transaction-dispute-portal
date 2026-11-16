using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Paging;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// An agent reading the recent transactions of the customer behind a dispute: what they need to judge "charged twice" or
/// "I don't recognise this". Scoped to a dispute that is still under review (Submitted or UnderReview), never a customer
/// list, and every page read is audited.
/// </summary>
public sealed class AgentCustomerTransactionQueries(
    IAgentCustomerTransactionRepository transactions,
    AgentCustomerTransactionCursorProtector cursors,
    TimeProvider time,
    SecurityMetrics securityMetrics,
    ILogger<AgentCustomerTransactionQueries> logger)
{
    /// <summary>How many days back the list reaches, unless the disputed transaction is older (disputes are allowed for longer).</summary>
    public const int WindowDays = 90;

    /// <summary>The audit event for an agent loading a page of a customer's transactions: ids, limit and count, never a row's content.</summary>
    public static readonly EventId CustomerTransactionsViewed = new(5104, "agent.customer_transactions.viewed");

    /// <summary>
    /// One page. A page that was read is audited once, whatever number of rows it holds; a refused request (no such
    /// dispute, bad cursor) read nothing and is not.
    /// </summary>
    /// <exception cref="Shared.Errors.NotFoundException">No dispute has this id, or it is no longer under review.</exception>
    /// <exception cref="Shared.Errors.InvalidCursorException">Tampered, another agent's, or another dispute's cursor.</exception>
    public async Task<AgentCustomerTransactionsResponse> ListAsync(
        Guid disputeId, AgentCustomerTransactionsRequest request, string agentSubject, string traceId, CancellationToken cancellationToken)
    {
        // Only a dispute still under review opens its customer's transactions; a decided or withdrawn one is answered as a
        // missing id, so a closed case's id cannot be used to read the customer's history.
        AgentDisputeScope scope = await transactions.FindScopeAsync(disputeId, cancellationToken) ?? throw DisputeNotFound.ToAgent();
        if (!DisputeLifecycle.ActiveStatuses.Contains(scope.Status))
        {
            throw DisputeNotFound.ToAgent();
        }

        TransactionKey? after = string.IsNullOrEmpty(request.Cursor) ? null : cursors.Unprotect(request.Cursor, agentSubject, disputeId);
        int limit = PageLimit.ParseOrDefault(request.Limit);
        DateOnly from = WindowStart(scope.DisputedTransactionDate);
        // One extra row says whether a next page exists, without a count query.
        IReadOnlyList<AgentCustomerTransaction> rows = await transactions.ListAsync(
            scope.CustomerId, new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), after, limit + 1, cancellationToken);
        string? nextCursor = rows.Count > limit
            ? cursors.Protect(new TransactionKey(rows[limit - 1].TransactionDate, rows[limit - 1].Id), agentSubject, disputeId)
            : null;
        AgentCustomerTransactionsResponse page = new([.. rows.Take(limit)], nextCursor, from);
        securityMetrics.Audit(SecurityMetrics.AgentCustomerTransactionsViewed);
        logger.LogInformation(
            CustomerTransactionsViewed,
            "agent.customer_transactions.viewed dispute {DisputeId} limit {Limit} returned {Returned} by {AgentSubject} trace {TraceId}",
            disputeId,
            limit,
            page.Items.Count,
            agentSubject,
            traceId);
        return page;
    }

    /// <summary>
    /// The first UTC day listed: the earlier of <see cref="WindowDays"/> days ago and the day of the disputed transaction,
    /// so a dispute raised late (the window for it is longer) still lists the transaction it is about.
    /// </summary>
    private DateOnly WindowStart(DateTimeOffset disputedTransactionDate)
    {
        DateOnly recent = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-WindowDays);
        DateOnly disputed = DateOnly.FromDateTime(disputedTransactionDate.UtcDateTime);
        return disputed < recent ? disputed : recent;
    }
}
