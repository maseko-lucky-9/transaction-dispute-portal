using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <param name="Amount">A decimal string ("123.45"), never a JSON number.</param>
/// <param name="DisputeBlockReasons">
/// Everything that stops a dispute: already_disputed (an active, Accepted or Rejected dispute exists), then the
/// eligibility rules (not_posted, not_debit, window_expired, too_many_active_disputes, too_many_disputes_for_transaction);
/// empty when disputable.
/// </param>
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
