using DisputePortal.Api.Shared.Paging;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>The validated queue filter: one status, or the open queue (Submitted and UnderReview) when none is given.</summary>
public sealed record AgentQueueFilter(DisputeStatus? Status)
{
    public static AgentQueueFilter Parse(AgentQueueRequest request) =>
        new(request.Status is null ? null : Enum.Parse<DisputeStatus>(request.Status, ignoreCase: true));

    /// <summary>The status name, or "open" when none is given: a closed set, so it is safe in a log line.</summary>
    public string Label => Status?.ToString() ?? "open";

    /// <summary>Binds a cursor to the filter it was issued for ("open" when no status is given).</summary>
    public string Fingerprint() => KeysetCursor.Fingerprint($"agent-queue;status={Label}");
}
