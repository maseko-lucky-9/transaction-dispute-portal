namespace DisputePortal.Api.Features.Transactions;

/// <summary>The transaction's current dispute. Always null until the Disputes slice lands.</summary>
public sealed record DisputeSummary(Guid Id, string Status);
