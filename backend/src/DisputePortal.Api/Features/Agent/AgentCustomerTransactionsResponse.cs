namespace DisputePortal.Api.Features.Agent;

/// <param name="NextCursor">Pass back as <c>cursor</c> for the next page; null on the last page.</param>
/// <param name="From">The first day of the window (UTC): the transactions listed are those on or after it. A date, not a count.</param>
public sealed record AgentCustomerTransactionsResponse(IReadOnlyList<AgentCustomerTransaction> Items, string? NextCursor, DateOnly From);
