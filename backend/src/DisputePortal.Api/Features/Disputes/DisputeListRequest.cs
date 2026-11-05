using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>The history query, bound as strings like the transaction list so a bad value is a field error.</summary>
/// <param name="Status">Submitted, UnderReview, Accepted, Rejected or Withdrawn; absent means every status.</param>
/// <param name="Limit">Page size, 1 to 100; 25 when absent.</param>
/// <param name="Cursor">The <c>nextCursor</c> of the previous page, unchanged. Absent or empty means the first page.</param>
public sealed record DisputeListRequest(
    [FromQuery(Name = "status")] string? Status,
    [FromQuery(Name = "limit")] string? Limit,
    [FromQuery(Name = "cursor")] string? Cursor);
