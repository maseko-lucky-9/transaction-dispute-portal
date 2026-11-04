namespace DisputePortal.Domain.Transactions;

/// <summary>
/// What the eligibility rules may know about a transaction. Transaction is a read model fed by core banking, outside the
/// Dispute aggregate, so the rules read a snapshot and never change it.
/// </summary>
public sealed record TransactionSnapshot(TransactionStatus Status, TransactionDirection Direction, DateTimeOffset TransactionDate);
