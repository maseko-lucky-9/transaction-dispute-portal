using DisputePortal.Api.Features.Disputes;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// The Dispute aggregate seen from the back office: every customer's disputes. One of the two repositories (the other is
/// <see cref="IAgentCustomerTransactionRepository"/>) that lift the Owner filter, only for the routes the DisputeAgent policy guards.
/// </summary>
public interface IAgentDisputeRepository
{
    /// <summary>The aggregate with its history, tracked for a change; null when no dispute has this id.</summary>
    Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The current row version, read fresh; null when no dispute has this id.</summary>
    Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The oldest Submitted dispute that no other transaction has locked, locked FOR UPDATE until the current transaction
    /// ends and loaded with its history for a change; null when every Submitted dispute is taken or there is none.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called outside a transaction, where the lock would end with the statement.</exception>
    Task<Dispute?> LockOldestUnclaimedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="take"/> queue items after <paramref name="after"/>, oldest first. The assignment filter and
    /// each item's assignment are relative to <paramref name="agentSubject"/>.
    /// </summary>
    Task<IReadOnlyList<AgentQueueItem>> ListAsync(AgentQueueFilter filter, string agentSubject, DisputeKey? after, int take, CancellationToken cancellationToken);

    /// <summary>The back-office detail, its assignment relative to <paramref name="agentSubject"/>; null when no dispute has this id.</summary>
    Task<VersionedAgentDispute?> GetDetailAsync(Guid id, string agentSubject, CancellationToken cancellationToken);
}
