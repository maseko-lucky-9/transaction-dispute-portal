using System.Text.Json.Serialization;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>
/// The submit body. Fields bind as strings so a malformed id or amount is a field error (400 validation_failed with
/// errors{}) rather than an unreadable body; unknown fields are refused (400 malformed_request), so a typo surfaces.
/// </summary>
/// <param name="Id">A UUID the client generates when the form opens and reuses on every retry (the idempotency key).</param>
/// <param name="Description">10 to 2000 characters after trimming, with no card or ID number in it.</param>
/// <param name="Amount">A decimal string with at most two decimals ("125.50"); absent means the whole transaction amount.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateDisputeRequest(string? Id, string? TransactionId, string? ReasonCode, string? Description, string? Amount);
