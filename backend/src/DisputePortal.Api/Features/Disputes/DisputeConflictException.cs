using DisputePortal.Api.Shared.Errors;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>A submission that conflicts with what exists (409, ADR-007).</summary>
public sealed class DisputeConflictException(string message, string code, Guid? existingDisputeId = null)
    : ApiException(message, StatusCodes.Status409Conflict, code)
{
    public const string DisputeExists = "dispute_exists";
    public const string IdempotencyKeyReuse = "idempotency_key_reuse";
    public const string DisputeIdConflict = "dispute_id_conflict";

    public Guid? ExistingDisputeId { get; } = existingDisputeId;

    public static DisputeConflictException Exists(Guid existingDisputeId) =>
        new("This transaction already has a dispute; open it instead.", DisputeExists, existingDisputeId);

    public static DisputeConflictException KeyReused() =>
        new("This dispute id was already used for another transaction; open a new form.", IdempotencyKeyReuse);

    // The id belongs to someone else's dispute: say only that it is taken, never whose or what.
    public static DisputeConflictException IdTaken() =>
        new("This dispute id cannot be used; open a new form.", DisputeIdConflict);

    public override ProblemDetails ToProblem() =>
        new DisputeConflictProblem { Status = Status, Detail = Message, ExistingDisputeId = ExistingDisputeId, Extensions = { ["code"] = Code } };
}
