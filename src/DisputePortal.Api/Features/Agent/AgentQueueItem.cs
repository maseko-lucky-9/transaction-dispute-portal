using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>One dispute in the work queue. No description: the agent opens the detail to read it (and that view is audited).</summary>
/// <param name="Amount">The disputed amount as a decimal string.</param>
/// <param name="CustomerDisplayName">The customer's display name only, never their subject or contact details.</param>
/// <param name="MaskedAccountNumber">Last four digits only, e.g. "**** 4821".</param>
public sealed record AgentQueueItem(
    Guid Id,
    DisputeStatus Status,
    DateTimeOffset CreatedAt,
    string MerchantName,
    string Amount,
    string Currency,
    string ReasonCode,
    string CustomerDisplayName,
    string MaskedAccountNumber);
