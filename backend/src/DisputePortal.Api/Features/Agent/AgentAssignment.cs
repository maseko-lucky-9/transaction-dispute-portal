namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// Who is reviewing a dispute, relative to the agent asking (ADR-019). Absent (null) once the dispute is decided or
/// withdrawn. Sent as its name.
/// </summary>
public enum AgentAssignment
{
    /// <summary>Submitted: in the queue, nobody reviewing it.</summary>
    Unclaimed,

    /// <summary>UnderReview, and the asking agent is reviewing it.</summary>
    You,

    /// <summary>UnderReview by someone else (or by nobody recorded, for a row an older version put under review).</summary>
    AnotherAgent,
}
