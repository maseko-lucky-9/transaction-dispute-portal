namespace DisputePortal.Api.Features.Transactions;

/// <param name="MaskedNumber">Only the last four digits, e.g. "**** 4821".</param>
public sealed record TransactionAccount(string MaskedNumber);
