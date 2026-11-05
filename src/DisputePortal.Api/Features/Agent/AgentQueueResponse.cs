namespace DisputePortal.Api.Features.Agent;

/// <param name="NextCursor">Pass back as <c>cursor</c> for the next page; null on the last page.</param>
public sealed record AgentQueueResponse(IReadOnlyList<AgentQueueItem> Items, string? NextCursor);
