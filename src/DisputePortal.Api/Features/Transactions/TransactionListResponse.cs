namespace DisputePortal.Api.Features.Transactions;

/// <param name="NextCursor">Pass back as <c>cursor</c> for the next page; null on the last page.</param>
public sealed record TransactionListResponse(IReadOnlyList<TransactionItem> Items, string? NextCursor);
