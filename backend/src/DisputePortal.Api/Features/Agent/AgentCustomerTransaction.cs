using DisputePortal.Api.Features.Transactions;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// One transaction of the customer behind a dispute, as an agent reads it. The transaction's own facts, the masked
/// account and the dispute state only: no eligibility flags, and never the customer's subject or any other personal data.
/// </summary>
/// <param name="Amount">A decimal string ("123.45"), never a JSON number.</param>
/// <param name="MaskedAccountNumber">Last four digits only, e.g. "**** 4821".</param>
/// <param name="Dispute">The transaction's current dispute, if any: the active one, else the latest.</param>
public sealed record AgentCustomerTransaction(
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
    string MaskedAccountNumber,
    DisputeSummary? Dispute);
