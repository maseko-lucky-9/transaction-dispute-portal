using DisputePortal.Domain.Common;

namespace DisputePortal.UnitTests;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("0", "ZAR")]
    [InlineData("1250.75", "ZAR")]
    [InlineData("0.0001", "USD")]
    [InlineData("999999999999999.9999", "EUR")]
    public void AValidAmountAndCurrencyMakeMoney(string amount, string currency)
    {
        Money money = Money.Of(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency);

        Assert.Equal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), money.Amount);
        Assert.Equal(currency, money.Currency);
    }

    [Theory]
    [InlineData("zar")]
    [InlineData("ZA")]
    [InlineData("ZARR")]
    [InlineData("Z1R")]
    [InlineData("")]
    public void ACurrencyMustBeThreeUpperCaseLetters(string currency)
    {
        DomainException refused = Assert.Throws<DomainException>(() => Money.Of(1m, currency));

        Assert.Equal("money_invalid_currency", refused.Code);
    }

    [Fact]
    public void AnAmountCannotBeNegative() =>
        Assert.Equal("money_negative", Assert.Throws<DomainException>(() => Money.Of(-0.01m, "ZAR")).Code);

    [Fact]
    public void AnAmountHasAtMostFourDecimalPlaces() =>
        Assert.Equal("money_too_precise", Assert.Throws<DomainException>(() => Money.Of(1.00001m, "ZAR")).Code);

    [Fact]
    public void TrailingZerosBeyondFourPlacesAreNotExtraPrecision() =>
        Assert.Equal(1.5m, Money.Of(1.500000m, "ZAR").Amount);

    [Fact]
    public void AnAmountMustFitNumeric19Scale4() =>
        Assert.Equal("money_out_of_range", Assert.Throws<DomainException>(() => Money.Of(1_000_000_000_000_000m, "ZAR")).Code);

    [Fact]
    public void AddingKeepsTheCurrency() =>
        Assert.Equal(Money.Of(15.25m, "ZAR"), Money.Of(10m, "ZAR").Add(Money.Of(5.25m, "ZAR")));

    [Fact]
    public void DifferentCurrenciesCannotBeCombined()
    {
        Assert.Equal("money_currency_mismatch", Assert.Throws<DomainException>(() => Money.Of(1m, "ZAR").Add(Money.Of(1m, "USD"))).Code);
        Assert.Equal("money_currency_mismatch", Assert.Throws<DomainException>(() => Money.Of(1m, "ZAR").IsGreaterThan(Money.Of(1m, "USD"))).Code);
    }

    [Fact]
    public void ComparisonIsByAmountWithinACurrency()
    {
        Assert.True(Money.Of(10m, "ZAR").IsGreaterThan(Money.Of(9.99m, "ZAR")));
        Assert.False(Money.Of(10m, "ZAR").IsGreaterThan(Money.Of(10.00m, "ZAR")));
    }

    [Fact]
    public void EqualAmountsAreEqualWhateverTheirScale() =>
        Assert.Equal(Money.Of(10m, "ZAR"), Money.Of(10.00m, "ZAR"));

    [Fact]
    public void ZeroIsZeroInTheGivenCurrency() =>
        Assert.Equal(Money.Of(0m, "ZAR"), Money.Zero("ZAR"));

    [Fact]
    public void ToStringIsInvariantWithAtLeastTwoDecimals() =>
        Assert.Equal("1234.50 ZAR", Money.Of(1234.5m, "ZAR").ToString());
}
