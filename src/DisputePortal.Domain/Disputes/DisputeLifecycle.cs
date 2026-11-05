namespace DisputePortal.Domain.Disputes;

/// <summary>
/// The state machine as data (ADR-003): a move is legal only if (from, to, actor) is a row of the table. A new move is a
/// new row; everything absent is refused.
/// </summary>
public static class DisputeLifecycle
{
    private static readonly HashSet<(DisputeStatus From, DisputeStatus To, ActorType Actor)> Transitions =
    [
        (DisputeStatus.Submitted, DisputeStatus.UnderReview, ActorType.Agent),
        (DisputeStatus.UnderReview, DisputeStatus.Accepted, ActorType.Agent),
        (DisputeStatus.UnderReview, DisputeStatus.Rejected, ActorType.Agent),
        (DisputeStatus.Submitted, DisputeStatus.Withdrawn, ActorType.Customer),
        (DisputeStatus.UnderReview, DisputeStatus.Withdrawn, ActorType.Customer),
    ];

    /// <summary>The statuses the partial unique index covers: at most one per transaction.</summary>
    public static IReadOnlyList<DisputeStatus> ActiveStatuses { get; } = [DisputeStatus.Submitted, DisputeStatus.UnderReview];

    /// <summary>The statuses that stop a new dispute on the same transaction: all but Withdrawn.</summary>
    public static IReadOnlyList<DisputeStatus> BlockingStatuses { get; } =
        [DisputeStatus.Submitted, DisputeStatus.UnderReview, DisputeStatus.Accepted, DisputeStatus.Rejected];

    public static bool Allows(DisputeStatus from, DisputeStatus to, ActorType actor) => Transitions.Contains((from, to, actor));

    public static bool IsTerminal(DisputeStatus status) => status is DisputeStatus.Accepted or DisputeStatus.Rejected or DisputeStatus.Withdrawn;

    /// <summary>An agent's decision: it must say why (the resolution note).</summary>
    public static bool IsResolution(DisputeStatus status) => status is DisputeStatus.Accepted or DisputeStatus.Rejected;
}
