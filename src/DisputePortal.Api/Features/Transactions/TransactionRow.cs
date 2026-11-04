using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>The read projection the repository returns: one transaction with its account's masked number.</summary>
public sealed record TransactionRow(
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
    string MaskedNumber);
