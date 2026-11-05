namespace DisputePortal.Api.Features.Disputes;

/// <summary>The reason catalogue: reference data, the same for every customer.</summary>
public interface IDisputeReasonRepository
{
    Task<IReadOnlyList<DisputeReason>> ListAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string code, CancellationToken cancellationToken);
}
