using DisputePortal.Api.Features.Transactions;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// A disputed customer's transactions seen from the back office. Lifts the Owner filter like <see cref="IAgentDisputeRepository"/>,
/// and only for the route the DisputeAgent policy guards; the customer always comes from a dispute row, never from the client.
/// </summary>
public interface IAgentCustomerTransactionRepository
{
    /// <summary>The customer, the disputed transaction's date and the dispute's status; null when no dispute has this id.</summary>
    Task<AgentDisputeScope?> FindScopeAsync(Guid disputeId, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="take"/> of the customer's transactions dated on or after <paramref name="fromInclusive"/> and
    /// before <paramref name="after"/>, newest first.
    /// </summary>
    Task<IReadOnlyList<AgentCustomerTransaction>> ListAsync(
        Guid customerId, DateTimeOffset fromInclusive, TransactionKey? after, int take, CancellationToken cancellationToken);
}
