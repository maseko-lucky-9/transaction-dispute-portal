namespace DisputePortal.Api.Features.Disputes;

/// <param name="Created">False for an idempotent replay of an id already submitted for the same transaction (200, not 201).</param>
public sealed record SubmitOutcome(VersionedDispute Dispute, bool Created);
