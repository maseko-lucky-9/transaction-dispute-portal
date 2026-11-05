namespace DisputePortal.Api.Features.Transactions;

/// <summary>A position in the keyset order (transaction_date desc, id desc): the last row of a page.</summary>
public sealed record TransactionKey(DateTimeOffset TransactionDate, Guid Id);
