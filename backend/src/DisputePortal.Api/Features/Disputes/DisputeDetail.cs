using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>A dispute with its description, resolution and timeline (oldest first). Sent with <c>ETag</c>.</summary>
/// <param name="Amount">The disputed amount as a decimal string.</param>
/// <param name="ResolutionNote">The agent's reason for accepting or rejecting; null otherwise.</param>
public sealed record DisputeDetail(
    Guid Id,
    Guid TransactionId,
    string MerchantName,
    string Amount,
    string Currency,
    string ReasonCode,
    DisputeStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    string Description,
    string? ResolutionNote,
    IReadOnlyList<TimelineEntry> Timeline,
    TransactionSummary Transaction);
