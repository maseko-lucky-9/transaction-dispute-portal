using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Transactions;

public sealed class TransactionRepository(DisputePortalDbContext db) : ITransactionRepository
{
    public async Task<IReadOnlyList<TransactionRow>> ListAsync(TransactionFilter filter, TransactionKey? after, int take, CancellationToken cancellationToken) =>
        await PageQuery(filter, after, take).ToListAsync(cancellationToken);

    public Task<TransactionRow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Project(db.Transactions.AsNoTracking().Where(transaction => transaction.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<TransactionSnapshot?> GetSnapshotAsync(Guid id, CancellationToken cancellationToken) =>
        db.Transactions.AsNoTracking()
            .Where(transaction => transaction.Id == id)
            .Select(transaction => new TransactionSnapshot(
                transaction.Status, transaction.Direction, transaction.TransactionDate, Money.Of(transaction.Amount, transaction.Currency)))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<int> CountActiveDisputesAsync(CancellationToken cancellationToken) =>
        db.Disputes.CountAsync(dispute => DisputeLifecycle.ActiveStatuses.Contains(dispute.Status), cancellationToken);

    /// <summary>
    /// The keyset page: a row-value comparison <c>(transaction_date, id) &lt; (last)</c> and the same order as the
    /// (customer_id, transaction_date desc, id desc) index, so Postgres seeks and never sorts (ADR-006). Internal so the
    /// EXPLAIN test plans exactly this query.
    /// </summary>
    internal IQueryable<TransactionRow> PageQuery(TransactionFilter filter, TransactionKey? after, int take)
    {
        IQueryable<Transaction> query = db.Transactions.AsNoTracking();
        if (filter.FromInclusive is { } from)
        {
            query = query.Where(transaction => transaction.TransactionDate >= from);
        }

        if (filter.ToExclusive is { } to)
        {
            query = query.Where(transaction => transaction.TransactionDate < to);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(transaction => transaction.Status == status);
        }

        if (after is not null)
        {
            DateTimeOffset afterDate = after.TransactionDate;
            Guid afterId = after.Id;
            query = query.Where(transaction => EF.Functions.LessThan(
                ValueTuple.Create(transaction.TransactionDate, transaction.Id), ValueTuple.Create(afterDate, afterId)));
        }

        return Project(query.OrderByDescending(transaction => transaction.TransactionDate).ThenByDescending(transaction => transaction.Id).Take(take));
    }

    // The masked number comes from a scalar subquery, not a join: EF wraps a join against the (also Owner-filtered)
    // accounts in a subquery and Postgres then hash-joins and re-sorts the page. The subquery keeps the plan a pure
    // Limit over the keyset index, evaluated only for the rows the page returns. The dispute columns are scalar
    // subqueries for the same reason, each a top-1 read of ix_disputes_transaction_id_customer_id_created_at_id with no
    // sort. "Most relevant" (active first, else latest) is simply the latest: a new dispute can only follow a Withdrawn
    // one, and nothing returns to an active status, so an active dispute is always a transaction's latest.
    private IQueryable<TransactionRow> Project(IQueryable<Transaction> query) =>
        query.Select(transaction => new TransactionRow(
            transaction.Id,
            transaction.Reference,
            transaction.MerchantName,
            transaction.Category,
            transaction.Amount,
            transaction.Currency,
            transaction.Direction,
            transaction.Status,
            transaction.TransactionDate,
            transaction.PostedAt,
            db.Accounts.Where(account => account.Id == transaction.AccountId).Select(account => account.MaskedNumber).FirstOrDefault(),
            db.Disputes.Where(dispute => dispute.TransactionId == transaction.Id)
                .OrderByDescending(dispute => dispute.CreatedAt)
                .ThenByDescending(dispute => dispute.Id)
                .Select(dispute => (Guid?)dispute.Id)
                .FirstOrDefault(),
            db.Disputes.Where(dispute => dispute.TransactionId == transaction.Id)
                .OrderByDescending(dispute => dispute.CreatedAt)
                .ThenByDescending(dispute => dispute.Id)
                .Select(dispute => (DisputeStatus?)dispute.Status)
                .FirstOrDefault(),
            db.Disputes.Count(dispute => dispute.TransactionId == transaction.Id)));
}
