namespace DisputePortal.Api.Features.Disputes;

/// <summary>One entry of the reason catalogue, in display order.</summary>
public sealed record DisputeReason(string Code, string Label, string Description);
