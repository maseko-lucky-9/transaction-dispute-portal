using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The real agent repository with a gate after loading the aggregate: racing agents both pass If-Match, then write
/// together; racing take-nexts are both inside their locking transaction, each holding its row lock, before either writes.
/// <paramref name="holdLock"/> keeps a take-next holding its lock that much longer after the gate, so a racer is sure to
/// try its write while the lock is held. <paramref name="beforeDetail"/> runs before each detail read (take next reads
/// it inside its transaction, still holding the row lock), so a test can act while the claim is not yet committed.
/// </summary>
public sealed class GatedAgentDisputeRepository(
    AgentDisputeRepository inner, Rendezvous afterFind, TimeSpan? holdLock = null, Func<CancellationToken, Task>? beforeDetail = null) : IAgentDisputeRepository
{
    public async Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Dispute? dispute = await inner.FindAsync(id, cancellationToken);
        await afterFind.ArriveAsync(cancellationToken);
        return dispute;
    }

    public async Task<Dispute?> LockOldestUnclaimedAsync(CancellationToken cancellationToken)
    {
        Dispute? dispute = await inner.LockOldestUnclaimedAsync(cancellationToken);
        await afterFind.ArriveAsync(cancellationToken);
        if (holdLock is { } hold)
        {
            await Task.Delay(hold, cancellationToken);
        }

        return dispute;
    }

    public Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken) => inner.GetVersionAsync(id, cancellationToken);

    public Task<IReadOnlyList<AgentQueueItem>> ListAsync(AgentQueueFilter filter, string agentSubject, DisputeKey? after, int take, CancellationToken cancellationToken) =>
        inner.ListAsync(filter, agentSubject, after, take, cancellationToken);

    public async Task<VersionedAgentDispute?> GetDetailAsync(Guid id, string agentSubject, CancellationToken cancellationToken)
    {
        if (beforeDetail is not null)
        {
            await beforeDetail(cancellationToken);
        }

        return await inner.GetDetailAsync(id, agentSubject, cancellationToken);
    }
}
