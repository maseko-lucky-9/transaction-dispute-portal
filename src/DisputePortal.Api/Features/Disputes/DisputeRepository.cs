using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Disputes;

public sealed class DisputeRepository(DisputePortalDbContext db) : IDisputeRepository
{
    public Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Disputes.Include(dispute => dispute.StatusChanges).SingleOrDefaultAsync(dispute => dispute.Id == id, cancellationToken);

    public Task<Guid?> FindTransactionIdAsync(Guid disputeId, CancellationToken cancellationToken) =>
        db.Disputes.AsNoTracking()
            .Where(dispute => dispute.Id == disputeId)
            .Select(dispute => (Guid?)dispute.TransactionId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<SubmissionFacts> GetSubmissionFactsAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        IQueryable<Dispute> disputes = db.Disputes.AsNoTracking();
        Guid? blocking = await disputes
            .Where(dispute => dispute.TransactionId == transactionId && DisputeLifecycle.BlockingStatuses.Contains(dispute.Status))
            .OrderByDescending(dispute => dispute.CreatedAt)
            .Select(dispute => (Guid?)dispute.Id)
            .FirstOrDefaultAsync(cancellationToken);
        int active = await disputes.CountAsync(dispute => DisputeLifecycle.ActiveStatuses.Contains(dispute.Status), cancellationToken);
        int forTransaction = await disputes.CountAsync(dispute => dispute.TransactionId == transactionId, cancellationToken);
        return new SubmissionFacts(blocking, active, forTransaction);
    }

    public void Add(Dispute dispute) => db.Disputes.Add(dispute);

    public Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        db.Disputes.AsNoTracking()
            .Where(dispute => dispute.Id == id)
            .Select(dispute => (uint?)dispute.Version)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The keyset page over (customer_id, created_at desc, id desc), like the transaction list: a row-value comparison
    /// and the index's own order, so Postgres seeks and never sorts. The merchant name is a scalar subquery, not a join.
    /// </summary>
    public async Task<IReadOnlyList<DisputeListItem>> ListAsync(
        DisputeListFilter filter, DisputeKey? after, int take, CancellationToken cancellationToken)
    {
        IQueryable<Dispute> query = db.Disputes.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(dispute => dispute.Status == status);
        }

        if (after is not null)
        {
            DateTimeOffset afterCreated = after.CreatedAt;
            Guid afterId = after.Id;
            query = query.Where(dispute => EF.Functions.LessThan(
                ValueTuple.Create(dispute.CreatedAt, dispute.Id), ValueTuple.Create(afterCreated, afterId)));
        }

        return await query
            .OrderByDescending(dispute => dispute.CreatedAt)
            .ThenByDescending(dispute => dispute.Id)
            .Take(take)
            .Select(dispute => new DisputeListItem(
                dispute.Id,
                dispute.TransactionId,
                db.Transactions.Where(transaction => transaction.Id == dispute.TransactionId).Select(transaction => transaction.MerchantName).First(),
                Money.FormatAmount(dispute.DisputedAmount.Amount),
                dispute.DisputedAmount.Currency,
                dispute.ReasonCode,
                dispute.Status,
                dispute.CreatedAt,
                dispute.UpdatedAt,
                dispute.ResolvedAt))
            .ToListAsync(cancellationToken);
    }

    // The composite foreign key guarantees the transaction is the same customer's, so the inner join never drops a row.
    public Task<VersionedDispute?> GetDetailAsync(Guid id, CancellationToken cancellationToken) =>
        db.Disputes.AsNoTracking()
            .Where(dispute => dispute.Id == id)
            .Join(db.Transactions, dispute => dispute.TransactionId, transaction => transaction.Id, (dispute, transaction) => new VersionedDispute(
                new DisputeDetail(
                    dispute.Id,
                    dispute.TransactionId,
                    transaction.MerchantName,
                    Money.FormatAmount(dispute.DisputedAmount.Amount),
                    dispute.DisputedAmount.Currency,
                    dispute.ReasonCode,
                    dispute.Status,
                    dispute.CreatedAt,
                    dispute.UpdatedAt,
                    dispute.ResolvedAt,
                    dispute.Description,
                    dispute.ResolutionNote,
                    dispute.StatusChanges
                        .OrderBy(change => change.OccurredAt)
                        .ThenBy(change => change.Id)
                        .Select(change => new TimelineEntry(change.From, change.To, change.ActorType, change.Note, change.OccurredAt))
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
}
