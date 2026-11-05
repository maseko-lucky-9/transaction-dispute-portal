using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Disputes;

/// <param name="Amount">The disputed amount as a decimal string ("125.50"), never a JSON number.</param>
/// <param name="ResolvedAt">When it reached Accepted, Rejected or Withdrawn; null while open.</param>
public sealed record DisputeListItem(
    Guid Id,
    Guid TransactionId,
    string MerchantName,
    string Amount,
    string Currency,
    string ReasonCode,
    DisputeStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);
