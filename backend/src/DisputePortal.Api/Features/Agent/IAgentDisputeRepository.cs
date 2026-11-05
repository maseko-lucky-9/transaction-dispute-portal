using DisputePortal.Api.Features.Disputes;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// The Dispute aggregate seen from the back office: every customer's disputes. The only repository that lifts the Owner
/// filter, and only for the routes the DisputeAgent policy guards.
/// </summary>
public interface IAgentDisputeRepository
{
    /// <summary>The aggregate with its history, tracked for a change; null when no dispute has this id.</summary>
    Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The current row version, read fresh; null when no dispute has this id.</summary>
    Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="take"/> queue items after <paramref name="after"/>, oldest first.</summary>
    Task<IReadOnlyList<AgentQueueItem>> ListAsync(AgentQueueFilter filter, DisputeKey? after, int take, CancellationToken cancellationToken);

    /// <summary>The back-office detail; null when no dispute has this id.</summary>
    Task<VersionedAgentDispute?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
}
