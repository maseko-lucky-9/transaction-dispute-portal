namespace DisputePortal.Api.Features.Disputes;

/// <summary>A detail read with the row version it was read at (the ETag).</summary>
public sealed record VersionedDispute(DisputeDetail Detail, uint Version);
