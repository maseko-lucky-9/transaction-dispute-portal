namespace DisputePortal.Api.Features.Transactions;

/// <summary>A customer's bank account. Only the masked number is ever stored or shown (POPIA).</summary>
public sealed class Account
{
    public const int MaskedNumberMaxLength = 32;

    public Guid Id { get; init; }

    public Guid CustomerId { get; init; }

    public string MaskedNumber { get; init; } = string.Empty;

    public string Currency { get; init; } = string.Empty;
}
