using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Validation;
using DisputePortal.Domain.Disputes;
using FluentValidation.Results;
using NSubstitute;

namespace DisputePortal.UnitTests;

public sealed class CreateDisputeRequestValidatorTests
{
    private const string Known = "duplicate_charge";
    private static readonly string Id = Guid.NewGuid().ToString();
    private static readonly string TransactionId = Guid.NewGuid().ToString();

    private readonly CreateDisputeRequestValidator validator;
    private readonly IDisputeReasonRepository reasons = Substitute.For<IDisputeReasonRepository>();

    public CreateDisputeRequestValidatorTests()
    {
        reasons.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<string>() == Known);
        validator = new CreateDisputeRequestValidator(reasons);
    }

    [Fact]
    public async Task AWellFormedRequestIsValid() => Assert.True((await ValidateAsync(Valid())).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
    public async Task IdsAreNonEmptyCanonicalUuids(string? value)
    {
        Assert.Contains("Id", await FailingFieldsAsync(Valid() with { Id = value }));
        Assert.Contains("TransactionId", await FailingFieldsAsync(Valid() with { TransactionId = value }));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("made_up", false)]
    [InlineData(Known, true)]
    public async Task TheReasonMustExist(string? reason, bool valid) =>
        Assert.Equal(valid, !(await FailingFieldsAsync(Valid() with { ReasonCode = reason })).Contains("ReasonCode"));

    // Bounded as a field error (400 validation_failed), and an overlong code is never looked up in the database.
    [Theory]
    [InlineData(40, true)]
    [InlineData(41, false)]
    [InlineData(5000, false)]
    public async Task TheReasonCodeHasAtMost40Characters(int length, bool lookedUp)
    {
        ValidationResult result = await ValidateAsync(Valid() with { ReasonCode = new string('r', length) });

        Assert.Equal(!lookedUp, result.Errors.Any(error => error.PropertyName == "ReasonCode" && error.ErrorMessage.Contains("at most 40", StringComparison.Ordinal)));
        await reasons.Received(lookedUp ? 1 : 0).ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public async Task TheDescriptionHas10To2000CharactersAfterTrimming(int length, bool valid) =>
        Assert.Equal(valid, !(await FailingFieldsAsync(Valid() with { Description = "   " + new string('d', length) + "   " })).Contains("Description"));

    [Theory]
    [InlineData("125.50", true)]
    [InlineData("125.5", true)]
    [InlineData("125", true)]
    [InlineData("0", true)]
    [InlineData("1234567890123", true)]
    [InlineData("12345678901234", false)]
    [InlineData("125.505", false)]
    [InlineData("-5", false)]
    [InlineData("1e2", false)]
    [InlineData("1,000", false)]
    [InlineData(".5", false)]
    [InlineData("5.", false)]
    [InlineData("007", false)]
    [InlineData(" 5", false)]
    public async Task TheAmountIsADecimalStringWithAtMostTwoDecimals(string amount, bool valid) =>
        Assert.Equal(valid, !(await FailingFieldsAsync(Valid() with { Amount = amount })).Contains("Amount"));

    // The failure carries its own problem code, so the API answers sensitive_data rather than validation_failed.
    [Fact]
    public async Task ACardNumberInTheDescriptionIsSensitiveData()
    {
        ValidationResult result = await ValidateAsync(Valid() with { Description = "My card 4111 1111 1111 1111 was charged twice" });

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal("Description", failure.PropertyName);
        Assert.Equal(new ProblemCode(SensitiveNumberDetector.Code), failure.CustomState);
    }

    // What is checked must be what is stored: the command stores Description.Trim(), and the length rule counts the
    // trimmed text. Padding must not push a plain card number past the scan (the scan limit is on the untrimmed text).
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5_000, 0)]
    [InlineData(0, 5_000)]
    [InlineData(SensitiveNumberDetector.MaxScanLength, 0)]
    [InlineData(SensitiveNumberDetector.MaxScanLength + 1, 0)]
    [InlineData(20_000, 20_000)]
    public async Task APaddedCardNumberIsStillSensitiveData(int leading, int trailing)
    {
        string description = new string(' ', leading) + "My card 4111 1111 1111 1111 was charged twice" + new string('\t', trailing);

        ValidationResult result = await ValidateAsync(Valid() with { Description = description });

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal(new ProblemCode(SensitiveNumberDetector.Code), failure.CustomState);
    }

    private static CreateDisputeRequest Valid() => new(Id, TransactionId, Known, "Charged twice for one order.", null);

    private Task<ValidationResult> ValidateAsync(CreateDisputeRequest request) => validator.ValidateAsync(request);

    private async Task<string[]> FailingFieldsAsync(CreateDisputeRequest request) =>
        [.. (await ValidateAsync(request)).Errors.Select(error => error.PropertyName)];
}
