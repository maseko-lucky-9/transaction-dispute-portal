using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>
/// The list query, bound as strings so a bad date or status comes back as a field error (400 validation_failed with
/// errors{}) rather than a bare binding failure.
/// </summary>
/// <param name="Limit">Page size, 1 to 100; 25 when absent.</param>
/// <param name="Cursor">The <c>nextCursor</c> of the previous page, unchanged.</param>
/// <param name="From">First day, inclusive, as yyyy-MM-dd (UTC).</param>
/// <param name="To">Last day, inclusive, as yyyy-MM-dd (UTC). At most 366 days after <paramref name="From"/>.</param>
/// <param name="Status">Pending, Posted or Reversed.</param>
public sealed record TransactionListRequest(
    [FromQuery(Name = "limit")] int? Limit,
    [FromQuery(Name = "cursor")] string? Cursor,
    [FromQuery(Name = "from")] string? From,
    [FromQuery(Name = "to")] string? To,
    [FromQuery(Name = "status")] string? Status);
