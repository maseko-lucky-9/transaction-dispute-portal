namespace DisputePortal.Api.Features.Me;

/// <param name="CustomerId">Null for a user with no customer record, such as a dispute agent.</param>
public sealed record MeResponse(Guid? CustomerId, string Subject, IReadOnlyList<string> Roles);
