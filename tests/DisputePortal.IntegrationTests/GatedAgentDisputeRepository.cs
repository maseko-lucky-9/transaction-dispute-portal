using DisputePortal.Api.Features.Agent;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.IntegrationTests;

/// <summary>The real agent repository with a gate after loading the aggregate: racing agents both pass If-Match, then write together.</summary>
public sealed class GatedAgentDisputeRepository(AgentDisputeRepository inner, Rendezvous afterFind) : IAgentDisputeRepository
{
    public async Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Dispute? dispute = await inner.FindAsync(id, cancellationToken);
        await afterFind.ArriveAsync(cancellationToken);
        return dispute;
    }

    public Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken) => inner.GetVersionAsync(id, cancellationToken);

    public Task<IReadOnlyList<AgentQueueItem>> ListAsync(AgentQueueFilter filter, DisputeKey? after, int take, CancellationToken cancellationToken) =>
        inner.ListAsync(filter, after, take, cancellationToken);

    public Task<VersionedAgentDispute?> GetDetailAsync(Guid id, CancellationToken cancellationToken) => inner.GetDetailAsync(id, cancellationToken);
}
