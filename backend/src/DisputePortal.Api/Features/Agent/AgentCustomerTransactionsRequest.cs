using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Agent;

/// <summary>The query of a dispute's customer-transactions list, bound as strings like the other lists so a bad value is a field error.</summary>
/// <param name="Limit">Page size, 1 to 100; 25 when absent.</param>
/// <param name="Cursor">The <c>nextCursor</c> of the previous page, unchanged. Absent or empty means the first page.</param>
public sealed record AgentCustomerTransactionsRequest(
    [FromQuery(Name = "limit")] string? Limit,
    [FromQuery(Name = "cursor")] string? Cursor);
