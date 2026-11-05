using System.Globalization;

namespace DisputePortal.Domain.Common;

/// <summary>
/// A non-negative amount in one ISO 4217 currency. The sign of a movement lives on the transaction's direction,
/// so an amount here is always a magnitude. Fits PostgreSQL numeric(19,4).
/// </summary>
public sealed record Money
{
    public const int MaxScale = 4;

    // numeric(19,4) holds 15 integer digits.
    private const decimal UpperBoundExclusive = 1_000_000_000_000_000m;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Of(decimal amount, string currency)
    {
        if (!IsCurrencyCode(currency))
        {
            throw new DomainException("money_invalid_currency", "The currency must be a three-letter ISO 4217 code in upper case.");
        }

        if (amount < 0)
        {
            throw new DomainException("money_negative", "An amount cannot be negative.");
        }

        if (amount >= UpperBoundExclusive)
        {
            throw new DomainException("money_out_of_range", "The amount is too large.");
        }

        if (amount.Scale > MaxScale && decimal.Round(amount, MaxScale) != amount)
        {
            throw new DomainException("money_too_precise", "An amount has at most four decimal places.");
        }

        return new Money(amount, currency);
    }

    public static Money Zero(string currency) => Of(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return Of(Amount + other.Amount, Currency);
    }

    public bool IsGreaterThan(Money other)
    {
        EnsureSameCurrency(other);
        return Amount > other.Amount;
    }

    public override string ToString() => FormatAmount(Amount) + " " + Currency;

    /// <summary>The wire form of an amount: an invariant decimal string with at least two decimals ("5.00", "123.4567"), never a float.</summary>
    public static string FormatAmount(decimal amount) => amount.ToString("0.00##", CultureInfo.InvariantCulture);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainException("money_currency_mismatch", "Amounts in different currencies cannot be combined.");
        }
    }

    private static bool IsCurrencyCode(string? currency) =>
        currency is { Length: 3 } && currency.All(character => character is >= 'A' and <= 'Z');
}
