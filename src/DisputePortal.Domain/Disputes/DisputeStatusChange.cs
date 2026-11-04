namespace DisputePortal.Domain.Disputes;

/// <summary>
/// One entry of a dispute's append-only history. Created only by <see cref="Dispute"/>; the app role may insert these rows
/// but never update or delete them, and cannot supply <see cref="OccurredAt"/>.
/// </summary>
public sealed class DisputeStatusChange
{
    internal DisputeStatusChange(Guid disputeId, DisputeStatus? from, DisputeStatus to, Actor actor, string? note)
    {
        DisputeId = disputeId;
        From = from;
        To = to;
        ActorType = actor.Type;
        ActorId = actor.Id;
        Note = note;
    }

    // For EF Core materialisation.
    private DisputeStatusChange()
    {
    }

    public long Id { get; private set; }

    public Guid DisputeId { get; private set; }

    /// <summary>Null on the first entry: the dispute did not exist before.</summary>
    public DisputeStatus? From { get; private set; }

    public DisputeStatus To { get; private set; }

    public ActorType ActorType { get; private set; }

    public string ActorId { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    /// <summary>Stamped by the database when the row is inserted (DEFAULT now()); the default value until then.</summary>
    public DateTimeOffset OccurredAt { get; private set; }
}
