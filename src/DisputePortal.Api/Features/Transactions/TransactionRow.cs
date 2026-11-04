using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>The read projection the repository returns: one transaction with its account's masked number.</summary>
/// <param name="MaskedNumber">Null only if the account is outside the customer's scope, which the composite foreign key
/// (account_id, customer_id) rules out; the mapper refuses it rather than hiding it.</param>
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
    string? MaskedNumber);
