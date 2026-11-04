using DisputePortal.Domain.Transactions;

namespace DisputePortal.Api.Features.Transactions;

/// <summary>
/// A read model: core banking feeds these rows and the app never writes them (the app role has SELECT only).
/// <see cref="CustomerId"/> is a denormalised copy of the account's customer, so ownership and the keyset index need no join.
/// </summary>
public sealed class Transaction
{
    public const int ReferenceMaxLength = 32;
    public const int MerchantNameMaxLength = 120;
    public const int CategoryMaxLength = 64;

    public Guid Id { get; init; }

    public Guid AccountId { get; init; }

    public Guid CustomerId { get; init; }

    public string Reference { get; init; } = string.Empty;

    public string MerchantName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Currency { get; init; } = string.Empty;

    public TransactionDirection Direction { get; init; }

    public TransactionStatus Status { get; init; }

    public DateTimeOffset TransactionDate { get; init; }

    public DateTimeOffset? PostedAt { get; init; }
}
