using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>The read projection the repository returns: one transaction with its account's masked number and dispute state.</summary>
/// <param name="MaskedNumber">Null only if the account is outside the customer's scope, which the composite foreign key
/// (account_id, customer_id) rules out; the mapper refuses it rather than hiding it.</param>
/// <param name="DisputeId">The most relevant dispute: the active one, else the latest; null when never disputed.</param>
/// <param name="DisputeCount">Every dispute ever raised on the transaction, withdrawn ones included.</param>
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
    string? MaskedNumber,
    Guid? DisputeId,
    DisputeStatus? DisputeStatus,
    int DisputeCount);
