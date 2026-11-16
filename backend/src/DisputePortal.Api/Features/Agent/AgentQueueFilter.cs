using DisputePortal.Api.Shared.Paging;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// The validated queue filter: one status, one assignment (ADR-019), or the open queue (Submitted and UnderReview) when
/// neither is given. The validator refuses both at once.
/// </summary>
public sealed record AgentQueueFilter(DisputeStatus? Status, AgentQueueAssignment? Assignment = null)
{
    public static AgentQueueFilter Parse(AgentQueueRequest request) => new(
        request.Status is null ? null : Enum.Parse<DisputeStatus>(request.Status, ignoreCase: true),
        request.Assignment is null ? null : Enum.Parse<AgentQueueAssignment>(request.Assignment, ignoreCase: true));

    /// <summary>The status name, "assignment=" and the assignment, or "open" when neither is given: a closed set, so it is safe in a log line.</summary>
    public string Label => Assignment is { } assignment ? "assignment=" + AssignmentName(assignment) : StatusLabel;

    /// <summary>
    /// Binds a cursor to the filter it was issued for. Without an assignment the text is exactly what it was before the
    /// assignment filter existed ("agent-queue;status=" and the status name or "open"), so cursors issued by an older
    /// version stay valid across a deploy.
    /// </summary>
    public string Fingerprint() => KeysetCursor.Fingerprint(Assignment is { } assignment
        ? $"agent-queue;assignment={AssignmentName(assignment)}"
        : $"agent-queue;status={StatusLabel}");

    private string StatusLabel => Status?.ToString() ?? "open";

    private static string AssignmentName(AgentQueueAssignment assignment) => assignment.ToString().ToLowerInvariant();
}
