namespace DisputePortal.Domain.Disputes;

/// <summary>Where a dispute is in its lifecycle (ADR-003). Stored and sent as its name.</summary>
public enum DisputeStatus
{
    Submitted,
    UnderReview,
    Accepted,
    Rejected,
    Withdrawn,
}
