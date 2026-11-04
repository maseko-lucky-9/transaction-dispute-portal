using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>The 409 body of a submission: dispute_exists (with the existing id), idempotency_key_reuse or dispute_id_conflict.</summary>
public sealed class DisputeConflictProblem : ProblemDetails
{
    /// <summary>The dispute that blocks this one; only on dispute_exists.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ExistingDisputeId { get; init; }
}
