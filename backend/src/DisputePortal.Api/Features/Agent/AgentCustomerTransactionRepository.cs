using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Agent;

public sealed class AgentCustomerTransactionRepository(DisputePortalDbContext db) : IAgentCustomerTransactionRepository
{
    // Agents work across customers: the Owner filter is lifted by name, for the whole query, so the accounts and disputes
    // it reaches are unfiltered too. Every query below therefore names its customer, taken from a dispute row.
    private static readonly string[] OwnerFilter = [DisputePortalDbContext.OwnerFilter];

    public Task<AgentDisputeScope?> FindScopeAsync(Guid disputeId, CancellationToken cancellationToken) =>
        db.Disputes.IgnoreQueryFilters(OwnerFilter).AsNoTracking()
            .Where(dispute => dispute.Id == disputeId)
            .Select(dispute => new AgentDisputeScope(
                dispute.CustomerId,
                db.Transactions.Where(transaction => transaction.Id == dispute.TransactionId).Select(transaction => transaction.TransactionDate).First(),
                dispute.Status))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The keyset page of <c>TransactionRepository</c>, rooted at one customer: a row-value comparison
    /// <c>(transaction_date, id) &lt; (last)</c> in the order of the (customer_id, transaction_date desc, id desc) index, so
    /// Postgres seeks and never sorts. The account and dispute columns are scalar subqueries for the same reason as there; a
    /// dispute subquery names the customer as the Owner filter would, so ix_disputes_transaction_id_customer_id_created_at_id
    /// supplies its order and each is a top-1 read with no sort.
    /// </summary>
    public async Task<IReadOnlyList<AgentCustomerTransaction>> ListAsync(
        Guid customerId, DateTimeOffset fromInclusive, TransactionKey? after, int take, CancellationToken cancellationToken)
    {
        IQueryable<Transaction> query = db.Transactions.IgnoreQueryFilters(OwnerFilter).AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId && transaction.TransactionDate >= fromInclusive);
        if (after is not null)
        {
            DateTimeOffset afterDate = after.TransactionDate;
            Guid afterId = after.Id;
            query = query.Where(transaction => EF.Functions.LessThan(
                ValueTuple.Create(transaction.TransactionDate, transaction.Id), ValueTuple.Create(afterDate, afterId)));
        }

        List<Row> rows = await query
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.Id)
            .Take(take)
            .Select(transaction => new Row(
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
                db.Accounts.Where(account => account.Id == transaction.AccountId).Select(account => account.MaskedNumber).First(),
                db.Disputes.Where(dispute => dispute.TransactionId == transaction.Id && dispute.CustomerId == customerId)
                    .OrderByDescending(dispute => dispute.CreatedAt)
                    .ThenByDescending(dispute => dispute.Id)
                    .Select(dispute => (Guid?)dispute.Id)
                    .FirstOrDefault(),
                db.Disputes.Where(dispute => dispute.TransactionId == transaction.Id && dispute.CustomerId == customerId)
                    .OrderByDescending(dispute => dispute.CreatedAt)
                    .ThenByDescending(dispute => dispute.Id)
                    .Select(dispute => (DisputeStatus?)dispute.Status)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToItem)];
    }

    private static AgentCustomerTransaction ToItem(Row row) => new(
        row.Id,
        row.Reference,
        row.MerchantName,
        row.Category,
        Money.FormatAmount(row.Amount),
        row.Currency,
        row.Direction,
        row.Status,
        row.TransactionDate,
        row.PostedAt,
        row.MaskedNumber,
        row.DisputeId is { } disputeId && row.DisputeStatus is { } disputeStatus ? new DisputeSummary(disputeId, disputeStatus) : null);

    // The most relevant dispute is the latest (see TransactionRepository: a new dispute can only follow a Withdrawn one).
    private sealed record Row(
        Guid Id,
        string Reference,
        string MerchantName,
        string Category,
        decimal Amount,
        string Currency,
        TransactionDirection Direction,
        TransactionStatus Status,
        DateTimeOffset TransactionDate,
        DateTimeOffset? PostedAt,
        string MaskedNumber,
        Guid? DisputeId,
        DisputeStatus? DisputeStatus);
}
