namespace DisputePortal.Api.Features.Disputes;

/// <param name="Amount">The whole transaction amount as a decimal string.</param>
public sealed record TransactionSummary(Guid Id, string Reference, string MerchantName, string Amount, string Currency, DateTimeOffset TransactionDate);
