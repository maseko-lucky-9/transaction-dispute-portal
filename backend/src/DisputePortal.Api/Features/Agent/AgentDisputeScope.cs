using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// What the customer-transactions list reads off a dispute row: whose transactions to list, where the disputed one falls,
/// and the dispute's status (the list is only for a dispute still under review).
/// </summary>
public sealed record AgentDisputeScope(Guid CustomerId, DateTimeOffset DisputedTransactionDate, DisputeStatus Status);
