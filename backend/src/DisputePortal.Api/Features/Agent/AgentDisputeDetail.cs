using DisputePortal.Api.Features.Disputes;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>A dispute as the back office sees it: the customer's view plus who they are and who acted. Sent with <c>ETag</c>.</summary>
/// <param name="Amount">The disputed amount as a decimal string.</param>
/// <param name="Assignment">Who is reviewing it, relative to the asking agent; null once decided or withdrawn.</param>
/// <param name="AssigneeId">The reviewing agent's subject while UnderReview; null otherwise. Never sent to customers.</param>
/// <param name="ClaimedAt">When the review started, while UnderReview; null otherwise.</param>
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
    TransactionSummary Transaction,
    AgentAssignment? Assignment,
    string? AssigneeId,
    DateTimeOffset? ClaimedAt);
