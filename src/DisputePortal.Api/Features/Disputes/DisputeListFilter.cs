using DisputePortal.Api.Shared.Paging;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>The validated history filter.</summary>
public sealed record DisputeListFilter(DisputeStatus? Status)
{
    public static DisputeListFilter Parse(DisputeListRequest request) =>
        new(request.Status is null ? null : Enum.Parse<DisputeStatus>(request.Status, ignoreCase: true));

    /// <summary>Binds a cursor to the filter it was issued for.</summary>
    public string Fingerprint() => KeysetCursor.Fingerprint($"status={Status}");
}
