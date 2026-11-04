using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <param name="Amount">A decimal string ("123.45"), never a JSON number.</param>
/// <param name="DisputeBlockReasons">Every rule that stops a dispute (not_posted, not_debit, window_expired); empty when disputable.</param>
/// <param name="Dispute">The transaction's current dispute, if any.</param>
public sealed record TransactionItem(
    Guid Id,
    string Reference,
    string MerchantName,
    string Category,
    string Amount,
    string Currency,
    TransactionDirection Direction,
    TransactionStatus Status,
    DateTimeOffset TransactionDate,
    DateTimeOffset? PostedAt,
    TransactionAccount Account,
    bool IsDisputable,
    IReadOnlyList<string> DisputeBlockReasons,
    DisputeSummary? Dispute);
