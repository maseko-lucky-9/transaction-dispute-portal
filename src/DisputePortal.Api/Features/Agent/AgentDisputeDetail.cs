using DisputePortal.Api.Features.Disputes;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>A dispute as the back office sees it: the customer's view plus who they are and who acted. Sent with <c>ETag</c>.</summary>
/// <param name="Amount">The disputed amount as a decimal string.</param>
public sealed record AgentDisputeDetail(
    Guid Id,
    Guid TransactionId,
    DisputeStatus Status,
    string ReasonCode,
    string Description,
    string Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote,
    string CustomerDisplayName,
    string MaskedAccountNumber,
    IReadOnlyList<AgentTimelineEntry> Timeline,
    TransactionSummary Transaction);
