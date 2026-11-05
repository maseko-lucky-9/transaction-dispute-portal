using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>The transaction's most relevant dispute: the active one, else the latest (a withdrawn one included).</summary>
public sealed record DisputeSummary(Guid Id, DisputeStatus Status);
