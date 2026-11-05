using DisputePortal.Api.Features.Disputes;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The real repository with a gate after the last read of a command: after the submission facts (state and counts),
/// or after loading the aggregate for a withdrawal. Concurrent requests all pass their checks, then write together.
/// A gate is a <see cref="Rendezvous"/> or an <see cref="AdvisoryLockRendezvous"/>.
/// </summary>
public sealed class GatedDisputeRepository(
    DisputeRepository inner, Func<CancellationToken, Task>? afterSubmissionFacts, Func<CancellationToken, Task>? afterFind) : IDisputeRepository
{
    public async Task<Dispute?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        Dispute? dispute = await inner.FindAsync(id, cancellationToken);
        if (afterFind is not null)
        {
            await afterFind(cancellationToken);
        }

        return dispute;
    }

    public Task<Guid?> FindTransactionIdAsync(Guid disputeId, CancellationToken cancellationToken) => inner.FindTransactionIdAsync(disputeId, cancellationToken);

    public async Task<SubmissionFacts> GetSubmissionFactsAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        SubmissionFacts facts = await inner.GetSubmissionFactsAsync(transactionId, cancellationToken);
        if (afterSubmissionFacts is not null)
        {
            await afterSubmissionFacts(cancellationToken);
        }

        return facts;
    }

    public void Add(Dispute dispute) => inner.Add(dispute);

    public Task<uint?> GetVersionAsync(Guid id, CancellationToken cancellationToken) => inner.GetVersionAsync(id, cancellationToken);

    public Task<IReadOnlyList<DisputeListItem>> ListAsync(DisputeListFilter filter, DisputeKey? after, int take, CancellationToken cancellationToken) =>
        inner.ListAsync(filter, after, take, cancellationToken);

    public Task<VersionedDispute?> GetDetailAsync(Guid id, CancellationToken cancellationToken) => inner.GetDetailAsync(id, cancellationToken);
}
