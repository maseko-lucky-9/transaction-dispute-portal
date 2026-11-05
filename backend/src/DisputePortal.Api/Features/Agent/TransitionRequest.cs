using System.Text.Json.Serialization;

namespace DisputePortal.Api.Features.Agent;

/// <summary>
/// An agent's move. Bound as strings so a bad value is a field error; unknown fields are refused. Whether the move is
/// legal from the current status is the lifecycle table's call (409 invalid_transition), not this shape's.
/// </summary>
/// <param name="ToStatus">UnderReview, Accepted or Rejected (agent moves); any other status name is 409.</param>
/// <param name="Note">Required (1 to 2000 characters, trimmed) to accept or reject; optional to start a review. The
/// customer sees it on their timeline, and an Accepted or Rejected note as the resolution.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransitionRequest(string? ToStatus, string? Note);
