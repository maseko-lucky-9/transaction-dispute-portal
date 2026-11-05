namespace DisputePortal.Domain.Transactions;

/// <summary>Money out of (Debit) or into (Credit) the customer's account. Stored and sent as its name.</summary>
public enum TransactionDirection
{
    Debit,
    Credit,
}
