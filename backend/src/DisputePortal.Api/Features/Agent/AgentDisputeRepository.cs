using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Agent;

public sealed class AgentDisputeRepository(DisputePortalDbContext db) : IAgentDisputeRepository
{
    // Agents work across customers: the Owner filter is lifted here and in AgentCustomerTransactionRepository, the two
    // places it is. By name, so any other filter still applies, and for the whole query, so the transactions, accounts
    // and history it reaches are unfiltered too.
    private static readonly string[] OwnerFilter = [DisputePortalDbContext.OwnerFilter];

    /// <summary>Take next's lock; internal so the privilege test runs exactly this statement as the app role.</summary>
    internal const string LockOldestUnclaimedSql =
        """SELECT id AS "Value" FROM disputes WHERE status = 'Submitted' ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED""";

    public Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        AllDisputes().Include(dispute => dispute.StatusChanges).SingleOrDefaultAsync(dispute => dispute.Id == id, cancellationToken);

    public Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        AllDisputes().AsNoTracking().Where(dispute => dispute.Id == id).Select(dispute => (uint?)dispute.Version).SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Take next (ADR-019): <c>FOR UPDATE SKIP LOCKED</c> hands concurrent callers different rows instead of queueing them
    /// on the oldest one. Not composed with LINQ: EF would wrap it in a subquery, where the locking clause does not belong.
    /// </summary>
    public async Task<Dispute?> LockOldestUnclaimedAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The row lock lasts until the transaction ends, so it needs one.");
        }

        List<Guid> oldest = await db.Database.SqlQueryRaw<Guid>(LockOldestUnclaimedSql).ToListAsync(cancellationToken);
        return oldest.Count == 0 ? null : await FindAsync(oldest[0], cancellationToken);
    }

    public async Task<IReadOnlyList<AgentQueueItem>> ListAsync(
        AgentQueueFilter filter, string agentSubject, DisputeKey? after, int take, CancellationToken cancellationToken) =>
        await PageQuery(filter, agentSubject, after, take).ToListAsync(cancellationToken);

    /// <summary>
    /// The FIFO page: a row-value comparison <c>(created_at, id) &gt; (last)</c> in index order, so Postgres seeks and never
    /// sorts. One status reads (status, created_at, id); the open queue reads a partial index on (created_at, id). The
    /// open statuses are constants in the SQL, so the planner can match that index's predicate. Internal so the EXPLAIN
    /// test plans exactly this query. The assignment filters are meant for (status, created_at, id) (unclaimed) and the
    /// partial index on (assignee_id, created_at, id) under review (mine); no EXPLAIN test checks those plans yet.
    /// </summary>
    internal IQueryable<AgentQueueItem> PageQuery(AgentQueueFilter filter, string agentSubject, DisputeKey? after, int take)
    {
        IQueryable<Dispute> query = Filtered(AllDisputes().AsNoTracking(), filter, agentSubject);
        if (after is not null)
        {
            DateTimeOffset afterCreated = after.CreatedAt;
            Guid afterId = after.Id;
            query = query.Where(dispute => EF.Functions.GreaterThan(
                ValueTuple.Create(dispute.CreatedAt, dispute.Id), ValueTuple.Create(afterCreated, afterId)));
        }

        // Scalar subqueries rather than joins, as on the customer lists: each is a lookup for the rows of the page only.
        return query
            .OrderBy(dispute => dispute.CreatedAt)
            .ThenBy(dispute => dispute.Id)
            .Take(take)
            .Select(dispute => new AgentQueueItem(
                dispute.Id,
                dispute.Status,
                dispute.CreatedAt,
                db.Transactions.Where(transaction => transaction.Id == dispute.TransactionId).Select(transaction => transaction.MerchantName).First(),
                Money.FormatAmount(dispute.DisputedAmount.Amount),
                dispute.DisputedAmount.Currency,
                dispute.ReasonCode,
                db.Customers.Where(customer => customer.Id == dispute.CustomerId).Select(customer => customer.DisplayName).First(),
                db.Transactions.Where(transaction => transaction.Id == dispute.TransactionId)
                    .Select(transaction => db.Accounts.Where(account => account.Id == transaction.AccountId).Select(account => account.MaskedNumber).First())
                    .First(),
                AssignmentOf(dispute.Status, dispute.AssigneeId, agentSubject),
                dispute.Status == DisputeStatus.UnderReview ? dispute.AssigneeId : null,
                dispute.Status == DisputeStatus.UnderReview ? dispute.UpdatedAt : null));
    }

    public Task<VersionedAgentDispute?> GetDetailAsync(Guid id, string agentSubject, CancellationToken cancellationToken) =>
        AllDisputes().AsNoTracking()
            .Where(dispute => dispute.Id == id)
            .Join(db.Transactions, dispute => dispute.TransactionId, transaction => transaction.Id, (dispute, transaction) => new VersionedAgentDispute(
                new AgentDisputeDetail(
                    dispute.Id,
                    dispute.TransactionId,
                    dispute.Status,
                    dispute.ReasonCode,
                    dispute.Description,
                    Money.FormatAmount(dispute.DisputedAmount.Amount),
                    dispute.DisputedAmount.Currency,
                    dispute.CreatedAt,
                    dispute.UpdatedAt,
                    dispute.ResolvedAt,
                    dispute.ResolutionNote,
                    db.Customers.Where(customer => customer.Id == dispute.CustomerId).Select(customer => customer.DisplayName).First(),
                    db.Accounts.Where(account => account.Id == transaction.AccountId).Select(account => account.MaskedNumber).First(),
                    dispute.StatusChanges
                        .OrderBy(change => change.OccurredAt)
                        .ThenBy(change => change.Id)
                        .Select(change => new AgentTimelineEntry(change.From, change.To, change.ActorType, change.ActorId, change.Note, change.OccurredAt))
                        .ToList(),
                    new TransactionSummary(
                        transaction.Id,
                        transaction.Reference,
                        transaction.MerchantName,
                        Money.FormatAmount(transaction.Amount),
                        transaction.Currency,
                        transaction.TransactionDate),
                    AssignmentOf(dispute.Status, dispute.AssigneeId, agentSubject),
                    dispute.Status == DisputeStatus.UnderReview ? dispute.AssigneeId : null,
                    dispute.Status == DisputeStatus.UnderReview ? dispute.UpdatedAt : null),
                dispute.Version))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Who is reviewing, as the asking agent sees it; worked out on the client from the row's status and assignee.
    /// Only an UnderReview row has a reviewer (ck_disputes_assignee holds an assignee exactly then), so the projections
    /// send assigneeId and claimedAt (updated_at: nothing else updates a row under review) only then.
    /// </summary>
    internal static AgentAssignment? AssignmentOf(DisputeStatus status, string? assigneeId, string agentSubject) => status switch
    {
        DisputeStatus.Submitted => AgentAssignment.Unclaimed,
        DisputeStatus.UnderReview => assigneeId == agentSubject ? AgentAssignment.You : AgentAssignment.AnotherAgent,
        _ => null,
    };

    /// <summary>
    /// The queue's rows for a filter. The statuses are constants in the SQL, so the planner can match the partial indexes.
    /// </summary>
    private static IQueryable<Dispute> Filtered(IQueryable<Dispute> query, AgentQueueFilter filter, string agentSubject) => filter switch
    {
        { Assignment: AgentQueueAssignment.Unclaimed } => query.Where(dispute => dispute.Status == DisputeStatus.Submitted),
        { Assignment: AgentQueueAssignment.Mine } =>
            query.Where(dispute => dispute.Status == DisputeStatus.UnderReview && dispute.AssigneeId == agentSubject),
        // "Others" is any assignee but the caller. EF's C# null semantics still emit `<> @agentSubject OR assignee_id IS
        // NULL`, the same SQL as before the contract step; the IS NULL arm matches nothing now (ck_disputes_assignee).
        { Assignment: AgentQueueAssignment.Others } =>
            query.Where(dispute => dispute.Status == DisputeStatus.UnderReview && dispute.AssigneeId != agentSubject),
        { Status: { } status } => query.Where(dispute => dispute.Status == status),
        _ => query.Where(dispute => dispute.Status == DisputeStatus.Submitted || dispute.Status == DisputeStatus.UnderReview),
    };

    private IQueryable<Dispute> AllDisputes() => db.Disputes.IgnoreQueryFilters(OwnerFilter);
}
