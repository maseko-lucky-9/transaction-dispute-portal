using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Agent;

public sealed class AgentDisputeRepository(DisputePortalDbContext db) : IAgentDisputeRepository
{
    // Agents work across customers: the one place the Owner filter is lifted. By name, so any other filter still
    // applies, and for the whole query, so the transactions, accounts and history it reaches are unfiltered too.
    private static readonly string[] OwnerFilter = [DisputePortalDbContext.OwnerFilter];

    public Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        AllDisputes().Include(dispute => dispute.StatusChanges).SingleOrDefaultAsync(dispute => dispute.Id == id, cancellationToken);

    public Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        AllDisputes().AsNoTracking().Where(dispute => dispute.Id == id).Select(dispute => (uint?)dispute.Version).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AgentQueueItem>> ListAsync(AgentQueueFilter filter, DisputeKey? after, int take, CancellationToken cancellationToken) =>
        await PageQuery(filter, after, take).ToListAsync(cancellationToken);

    /// <summary>
    /// The FIFO page: a row-value comparison <c>(created_at, id) &gt; (last)</c> in index order, so Postgres seeks and never
    /// sorts. One status reads (status, created_at, id); the open queue reads a partial index on (created_at, id). The
    /// open statuses are constants in the SQL, so the planner can match that index's predicate. Internal so the EXPLAIN
    /// test plans exactly this query.
    /// </summary>
    internal IQueryable<AgentQueueItem> PageQuery(AgentQueueFilter filter, DisputeKey? after, int take)
    {
        IQueryable<Dispute> query = AllDisputes().AsNoTracking();
        query = filter.Status is { } status
            ? query.Where(dispute => dispute.Status == status)
            : query.Where(dispute => dispute.Status == DisputeStatus.Submitted || dispute.Status == DisputeStatus.UnderReview);
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
                    .First()));
    }

    public Task<VersionedAgentDispute?> GetDetailAsync(Guid id, CancellationToken cancellationToken) =>
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
                        transaction.TransactionDate)),
                dispute.Version))
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<Dispute> AllDisputes() => db.Disputes.IgnoreQueryFilters(OwnerFilter);
}
