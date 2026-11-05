using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>
/// The Dispute aggregate's repository (ADR-002) and its read projections. The Owner filter scopes every query to the
/// current customer, so another customer's dispute is indistinguishable from a missing one.
/// </summary>
public interface IDisputeRepository
{
    /// <summary>The aggregate with its history, tracked for a change; null when missing or someone else's.</summary>
    Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The transaction an existing dispute id of this customer names: the idempotent-replay check. Never tracked.</summary>
    Task<Guid?> FindTransactionIdAsync(Guid disputeId, CancellationToken cancellationToken);

    /// <summary>The state and counts a submission is judged on, read just before the insert.</summary>
    Task<SubmissionFacts> GetSubmissionFactsAsync(Guid transactionId, CancellationToken cancellationToken);

    /// <summary>Queues a new dispute (and its first history entry) for the unit of work.</summary>
    void Add(Dispute dispute);

    /// <summary>The current row version, read fresh (not from a tracked copy); null when missing or someone else's.</summary>
    Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="take"/> disputes after <paramref name="after"/>, newest first.</summary>
    Task<IReadOnlyList<DisputeListItem>> ListAsync(DisputeListFilter filter, DisputeKey? after, int take, CancellationToken cancellationToken);

    /// <summary>The detail with its timeline and transaction; null when missing or someone else's.</summary>
    Task<VersionedDispute?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
}
