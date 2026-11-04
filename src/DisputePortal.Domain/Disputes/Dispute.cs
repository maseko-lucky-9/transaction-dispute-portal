using DisputePortal.Domain.Common;

namespace DisputePortal.Domain.Disputes;

/// <summary>
/// The aggregate root (ADR-003). Status moves only through <see cref="Withdraw"/> and <see cref="Transition"/>, which
/// consult <see cref="DisputeLifecycle"/>, and every move appends exactly one <see cref="DisputeStatusChange"/>.
/// </summary>
public sealed class Dispute
{
    public const int DescriptionMaxLength = 2000;
    public const int NoteMaxLength = 2000;

    private readonly List<DisputeStatusChange> statusChanges = [];

    // For EF Core materialisation.
    private Dispute()
    {
    }

    public Guid Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string ReasonCode { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Money DisputedAmount { get; private set; } = null!;

    public DisputeStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Set when the dispute reaches a terminal status, and only then.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>The agent's reason for accepting or rejecting; null otherwise.</summary>
    public string? ResolutionNote { get; private set; }

    /// <summary>The store's row version (PostgreSQL xmin), sent as the ETag. Maintained by the store, never by this class.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<DisputeStatusChange> StatusChanges => statusChanges;

    /// <summary>A new dispute in Submitted, with its first history entry (no previous status, actor the customer).</summary>
    /// <exception cref="DomainException">An empty id, a blank or overlong description, or a zero amount.</exception>
    public static Dispute Submit(DisputeSubmission submission, string customerSubject, DateTimeOffset now)
    {
        if (submission.Id == Guid.Empty)
        {
            throw new DomainException("dispute_id_required", "A dispute needs an id.");
        }

        if (string.IsNullOrWhiteSpace(submission.Description) || submission.Description.Length > DescriptionMaxLength)
        {
            throw new DomainException("description_invalid", $"A description has 1 to {DescriptionMaxLength} characters.");
        }

        if (submission.DisputedAmount.Amount <= 0)
        {
            throw new DomainException("amount_not_positive", "The disputed amount must be more than zero.");
        }

        Dispute dispute = new()
        {
            Id = submission.Id,
            TransactionId = submission.TransactionId,
            CustomerId = submission.CustomerId,
            ReasonCode = submission.ReasonCode,
            Description = submission.Description,
            DisputedAmount = submission.DisputedAmount,
            Status = DisputeStatus.Submitted,
            CreatedAt = now,
            UpdatedAt = now,
        };
        dispute.statusChanges.Add(new DisputeStatusChange(dispute.Id, null, DisputeStatus.Submitted, Actor.Customer(customerSubject), null));
        return dispute;
    }

    /// <exception cref="InvalidTransitionException">Terminal already, or the actor is not the customer.</exception>
    public void Withdraw(Actor actor, DateTimeOffset now) => Transition(DisputeStatus.Withdrawn, actor, note: null, now);

    /// <exception cref="InvalidTransitionException">(current, <paramref name="to"/>, actor) is not in the lifecycle table.</exception>
    /// <exception cref="DomainException">A resolution without a note, a note over 2000 characters, or a note with control characters or a card or ID number.</exception>
    public void Transition(DisputeStatus to, Actor actor, string? note, DateTimeOffset now)
    {
        if (!DisputeLifecycle.Allows(Status, to, actor.Type))
        {
            throw new InvalidTransitionException(Status, to, actor.Type);
        }

        string? checkedNote = CheckNote(to, note);
        statusChanges.Add(new DisputeStatusChange(Id, Status, to, actor, checkedNote));
        Status = to;
        UpdatedAt = now;
        if (DisputeLifecycle.IsTerminal(to))
        {
            ResolvedAt = now;
        }

        if (DisputeLifecycle.IsResolution(to))
        {
            ResolutionNote = checkedNote;
        }
    }

    private static string? CheckNote(DisputeStatus to, string? note)
    {
        string? trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is null && DisputeLifecycle.IsResolution(to))
        {
            throw new DomainException("resolution_note_required", "Accepting or rejecting a dispute needs a note.");
        }

        if (trimmed is { Length: > NoteMaxLength })
        {
            throw new DomainException("note_too_long", $"A note has at most {NoteMaxLength} characters.");
        }

        // Same free-text rules as a description, on the text that is stored (invalid_characters first). The length check
        // above bounds the scan.
        string? problem = SensitiveNumberDetector.ProblemCodeFor(trimmed);
        if (problem is not null)
        {
            throw new DomainException(problem, problem == SensitiveNumberDetector.InvalidCharactersCode
                ? "A note must not contain control characters."
                : "A note must not contain a card or ID number.");
        }

        return trimmed;
    }
}
