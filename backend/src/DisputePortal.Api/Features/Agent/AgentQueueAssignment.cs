namespace DisputePortal.Api.Features.Agent;

/// <summary>The queue's assignment filter (<c>?assignment=</c>, any case), relative to the agent asking (ADR-019).</summary>
public enum AgentQueueAssignment
{
    /// <summary>Submitted disputes, which nobody is reviewing.</summary>
    Unclaimed,

    /// <summary>Disputes under review by the asking agent.</summary>
    Mine,

    /// <summary>Disputes under review by anyone else, including the ones with no recorded reviewer.</summary>
    Others,
}
