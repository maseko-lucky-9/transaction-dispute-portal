using DisputePortal.Api.Features.Transactions;
using FluentValidation.Results;

namespace DisputePortal.UnitTests;

public sealed class TransactionListRequestValidatorTests
{
    private readonly TransactionListRequestValidator validator = new();

    [Fact]
    public void NoFiltersAtAllIsValid() => Assert.True(Validate(new TransactionListRequest(null, null, null, null, null)).IsValid);

    [Theory]
    [InlineData("1", true)]
    [InlineData("100", true)]
    [InlineData("0", false)]
    [InlineData("101", false)]
    [InlineData("-5", false)]
    [InlineData("abc", false)]
    [InlineData("1e2", false)]
    [InlineData("0x10", false)]
    [InlineData("+5", false)]
    [InlineData(" 5", false)]
    [InlineData("", false)]
    [InlineData("99999999999", false)]
    public void TheLimitIsAWholeNumberBetween1And100(string limit, bool valid) => AssertField(new(limit, null, null, null, null), "Limit", valid);

    [Theory]
    [InlineData("2026-01-31", true)]
    [InlineData("2026-02-30", false)]
    [InlineData("31/01/2026", false)]
    [InlineData("2026-1-31", false)]
    [InlineData("yesterday", false)]
    public void DatesAreIsoDays(string date, bool valid)
    {
        AssertField(new(null, null, date, null, null), "From", valid);
        AssertField(new(null, null, null, date, null), "To", valid);
    }

    // ToExclusive adds a day, so DateOnly.MaxValue once threw (500); the bounds keep every date well inside the range.
    [Theory]
    [InlineData("1900-01-01", true)]
    [InlineData("2100-12-31", true)]
    [InlineData("1899-12-31", false)]
    [InlineData("2101-01-01", false)]
    [InlineData("0001-01-01", false)]
    [InlineData("9999-12-31", false)]
    public void DatesLieBetween1900And2100(string date, bool valid)
    {
        AssertField(new(null, null, date, null, null), "From", valid);
        AssertField(new(null, null, null, date, null), "To", valid);
    }

    [Fact]
    public void BothBoundsAtTheMaximumDateAreRefused()
    {
        ValidationResult result = Validate(new(null, null, "9999-12-31", "9999-12-31", null));

        Assert.Contains(result.Errors, error => error.PropertyName == "From");
        Assert.Contains(result.Errors, error => error.PropertyName == "To");
    }

    [Fact]
    public void ToMayNotBeBeforeFrom() => AssertField(new(null, null, "2026-03-02", "2026-03-01", null), "To", valid: false);

    [Theory]
    [InlineData("2025-01-01", "2026-01-02", true)]
    [InlineData("2025-01-01", "2026-01-03", false)]
    public void TheRangeIsAtMost366Days(string from, string to, bool valid) => AssertField(new(null, null, from, to, null), "To", valid);

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("posted", true)]
    [InlineData("REVERSED", true)]
    [InlineData("Settled", false)]
    [InlineData("1", false)]
    public void TheStatusIsANamedValue(string status, bool valid) => AssertField(new(null, null, null, null, status), "Status", valid);

    [Fact]
    public void AnOverlongCursorIsRefused() =>
        AssertField(new(null, new string('a', TransactionListRequestValidator.MaxCursorLength + 1), null, null, null), "Cursor", valid: false);

    private ValidationResult Validate(TransactionListRequest request) => validator.Validate(request);

    private void AssertField(TransactionListRequest request, string field, bool valid) =>
        Assert.Equal(valid, !Validate(request).Errors.Any(error => error.PropertyName == field));
}
