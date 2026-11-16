using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>One dispute in the work queue. No description: the agent opens the detail to read it (and that view is audited).</summary>
/// <param name="Amount">The disputed amount as a decimal string.</param>
/// <param name="CustomerDisplayName">The customer's display name only, never their subject or contact details.</param>
/// <param name="MaskedAccountNumber">Last four digits only, e.g. "**** 4821".</param>
/// <param name="Assignment">Who is reviewing it, relative to the asking agent; null once decided or withdrawn.</param>
/// <param name="AssigneeId">The reviewing agent's subject while UnderReview (the same identifier the timeline's actorId
/// shows agents); null otherwise, and for a row an older version put under review. Never sent to customers.</param>
/// <param name="ClaimedAt">When the review started, while UnderReview; null otherwise.</param>
public sealed record AgentQueueItem(
    Guid Id,
    DisputeStatus Status,
    DateTimeOffset CreatedAt,
    string MerchantName,
    string Amount,
    string Currency,
    string ReasonCode,
    string CustomerDisplayName,
    string MaskedAccountNumber,
    AgentAssignment? Assignment,
    string? AssigneeId,
    DateTimeOffset? ClaimedAt);
