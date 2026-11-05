using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Agent;

/// <summary>The work-queue query, bound as strings like the customer lists so a bad value is a field error.</summary>
/// <param name="Status">One DisputeStatus; absent means the open queue (Submitted and UnderReview).</param>
/// <param name="Limit">Page size, 1 to 100; 25 when absent.</param>
/// <param name="Cursor">The <c>nextCursor</c> of the previous page, unchanged. Absent or empty means the first page.</param>
public sealed record AgentQueueRequest(
    [FromQuery(Name = "status")] string? Status,
    [FromQuery(Name = "limit")] string? Limit,
    [FromQuery(Name = "cursor")] string? Cursor);
