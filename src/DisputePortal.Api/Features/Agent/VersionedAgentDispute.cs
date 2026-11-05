namespace DisputePortal.Api.Features.Agent;

/// <summary>An agent detail read with the row version it was read at (the ETag).</summary>
public sealed record VersionedAgentDispute(AgentDisputeDetail Detail, uint Version);
