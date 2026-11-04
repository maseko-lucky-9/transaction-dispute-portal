namespace DisputePortal.Api.Features.Transactions;

/// <summary>Reads of the current customer's transactions; the Owner filter scopes every query. Never writes.</summary>
public interface ITransactionRepository
{
    /// <summary>Up to <paramref name="take"/> rows after <paramref name="after"/>, newest first.</summary>
    Task<IReadOnlyList<TransactionRow>> ListAsync(TransactionFilter filter, TransactionKey? after, int take, CancellationToken cancellationToken);

    /// <summary>Null when the transaction does not exist or belongs to someone else: the two must look the same.</summary>
    Task<TransactionRow?> GetAsync(Guid id, CancellationToken cancellationToken);
}
