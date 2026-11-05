using DisputePortal.Domain.Disputes;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>One status change as the customer sees it: the actor's type only, never who the agent was.</summary>
/// <param name="From">Null on the first entry (the submission).</param>
public sealed record TimelineEntry(DisputeStatus? From, DisputeStatus To, ActorType ActorType, string? Note, DateTimeOffset OccurredAt);
