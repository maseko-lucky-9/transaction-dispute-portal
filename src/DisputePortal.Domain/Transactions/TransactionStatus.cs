namespace DisputePortal.Domain.Transactions;

/// <summary>Where a transaction is in core banking. Stored and sent as its name.</summary>
public enum TransactionStatus
{
    Pending,
    Posted,
    Reversed,
}
