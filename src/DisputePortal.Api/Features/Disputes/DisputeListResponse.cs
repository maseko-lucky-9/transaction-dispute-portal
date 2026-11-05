namespace DisputePortal.Api.Features.Disputes;

/// <param name="NextCursor">Pass back as <c>cursor</c> for the next page; null on the last page.</param>
public sealed record DisputeListResponse(IReadOnlyList<DisputeListItem> Items, string? NextCursor);
