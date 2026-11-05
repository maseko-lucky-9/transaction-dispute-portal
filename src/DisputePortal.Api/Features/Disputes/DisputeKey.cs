namespace DisputePortal.Api.Features.Disputes;

/// <summary>A position in the history's keyset order (created_at desc, id desc): the last row of a page.</summary>
public sealed record DisputeKey(DateTimeOffset CreatedAt, Guid Id);
