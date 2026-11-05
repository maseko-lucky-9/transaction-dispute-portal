using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Agent;

/// <summary>One status change as the back office sees it: who acted (their subject), which customers never see.</summary>
/// <param name="ActorId">The identity provider subject of the customer or agent who made the change.</param>
public sealed record AgentTimelineEntry(DisputeStatus? From, DisputeStatus To, ActorType ActorType, string ActorId, string? Note, DateTimeOffset OccurredAt);
