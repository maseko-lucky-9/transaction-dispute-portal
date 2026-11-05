using DisputePortal.Domain.Common;

namespace DisputePortal.Domain.Disputes;

/// <summary>The move is not in the lifecycle table for this actor; the API answers 409, as it conflicts with the current state.</summary>
public sealed class InvalidTransitionException(DisputeStatus from, DisputeStatus to, ActorType actor)
    : DomainException(ErrorCode, $"A dispute that is {from} cannot be moved to {to} by the {actor.ToString().ToLowerInvariant()}.")
{
    public const string ErrorCode = "invalid_transition";

    public DisputeStatus From { get; } = from;

    public DisputeStatus To { get; } = to;

    public ActorType Actor { get; } = actor;
}
